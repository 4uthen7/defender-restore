using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace DefenderRestore
{
    internal enum St { Ok, Warn, Fail, Info, Skip }

    internal sealed class CheckItem
    {
        public string Name;
        public St State;
        public string Detail;
        public string Fix;

        public CheckItem(string name, St state, string detail, string fix)
        {
            Name = name;
            State = state;
            Detail = detail ?? "";
            Fix = fix;
        }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case St.Ok: return "正常";
                    case St.Warn: return "注意";
                    case St.Fail: return "要修復";
                    case St.Skip: return "対象外";
                    default: return "情報";
                }
            }
        }
    }

    internal static class DefenderUtil
    {
        public static bool IsAdmin()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal p = new WindowsPrincipal(id);
                    return p.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        public static string PlatformDir
        {
            get
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows Defender\Platform");
                if (!Directory.Exists(root)) return null;

                string best = null;
                Version bestVer = null;
                try
                {
                    foreach (string dir in Directory.GetDirectories(root))
                    {
                        string name = Path.GetFileName(dir);
                        if (!File.Exists(Path.Combine(dir, "MsMpEng.exe"))) continue;
                        string v = name;
                        int dash = v.IndexOf('-');
                        if (dash > 0) v = v.Substring(0, dash);
                        Version ver;
                        if (!Version.TryParse(v, out ver)) continue;
                        if (bestVer == null || ver > bestVer) { bestVer = ver; best = dir; }
                    }
                }
                catch { }

                if (best != null) return best;

                string current = Path.Combine(root, "CurrentVersion");
                if (File.Exists(Path.Combine(current, "MsMpEng.exe"))) return current;
                return null;
            }
        }

        public static string MsMpEng { get { string d = PlatformDir; return d == null ? null : Path.Combine(d, "MsMpEng.exe"); } }
        public static string MpCmdRun { get { string d = PlatformDir; return d == null ? null : Path.Combine(d, "MpCmdRun.exe"); } }

        public static bool BoolVal(object o)
        {
            if (o == null) return false;
            if (o is bool) return (bool)o;
            int i;
            if (int.TryParse(o.ToString(), out i)) return i != 0;
            return string.Equals(o.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        public static string RunProcess(string exe, string args, int timeoutMs, out int exitCode)
        {
            exitCode = -1;
            StringBuilder sb = new StringBuilder();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                using (Process p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        sb.AppendLine("(タイムアウト)");
                    }
                    exitCode = p.HasExited ? p.ExitCode : -1;
                    if (!string.IsNullOrEmpty(so)) sb.Append(so.Trim());
                    if (!string.IsNullOrEmpty(se)) sb.AppendLine().Append(se.Trim());
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine(ex.Message);
            }
            return sb.ToString().Trim();
        }

        public static string PowerShell(string script, int timeoutMs, out int exitCode)
        {
            string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(exe)) exe = "powershell.exe";
            return RunProcess(exe, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"", timeoutMs, out exitCode);
        }
    }

    internal static class Reg
    {
        public static RegistryView View
        {
            get { return Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32; }
        }

        private static RegistryKey Base()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, View);
        }

        public static RegistryKey Open(string path, bool writable)
        {
            RegistryKey b = Base();
            try { return b.OpenSubKey(path, writable); }
            finally { b.Dispose(); }
        }

        public static RegistryKey OpenReadAcl(string path)
        {
            RegistryKey b = Base();
            try
            {
                return b.OpenSubKey(path, RegistryKeyPermissionCheck.ReadSubTree,
                    RegistryRights.ReadPermissions);
            }
            finally { b.Dispose(); }
        }

        public static RegistryKey OpenWriteAcl(string path)
        {
            RegistryKey b = Base();
            try
            {
                return b.OpenSubKey(path, RegistryKeyPermissionCheck.ReadWriteSubTree,
                    RegistryRights.ReadPermissions | RegistryRights.ChangePermissions);
            }
            finally { b.Dispose(); }
        }

        public static object Get(string path, string name)
        {
            try
            {
                using (RegistryKey k = Open(path, false))
                    return k == null ? null : k.GetValue(name);
            }
            catch { return null; }
        }

        public static int GetDword(string path, string name, int def)
        {
            object v = Get(path, name);
            if (v == null) return def;
            try { return Convert.ToInt32(v); }
            catch { return def; }
        }

        public static bool Exists(string path)
        {
            try
            {
                using (RegistryKey k = Open(path, false)) return k != null;
            }
            catch { return false; }
        }

        public static bool SetDword(string path, string name, int value)
        {
            using (RegistryKey k = Open(path, true))
            {
                if (k == null) return false;
                k.SetValue(name, value, RegistryValueKind.DWord);
                return true;
            }
        }

        public static bool DeleteValue(string path, string name)
        {
            using (RegistryKey k = Open(path, true))
            {
                if (k == null) return false;
                if (Array.IndexOf(k.GetValueNames(), name) < 0) return false;
                k.DeleteValue(name, false);
                return true;
            }
        }

        public static string[] ValueNames(string path)
        {
            try
            {
                using (RegistryKey k = Open(path, false))
                    return k == null ? new string[0] : k.GetValueNames();
            }
            catch { return new string[0]; }
        }
    }

    internal static class Svc
    {
        private const uint SC_MANAGER_CONNECT = 0x0001;
        private const uint SERVICE_QUERY_STATUS = 0x0004;
        private const uint SERVICE_CHANGE_CONFIG = 0x0002;
        private const uint SERVICE_START = 0x0010;
        private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
        private const uint SERVICE_RUNNING = 0x00000004;
        private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string serviceName, uint access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ChangeServiceConfig(IntPtr hService, uint serviceType, uint startType,
            uint errorControl, string binaryPathName, string loadOrderGroup, IntPtr tagId,
            string dependencies, string serviceStartName, string password, string displayName);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool StartService(IntPtr hService, int numArgs, IntPtr args);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatus(IntPtr hService, ref SERVICE_STATUS status);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr hService);

        public static bool Exists(string name)
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return false;
            IntPtr svc = IntPtr.Zero;
            try
            {
                svc = OpenService(scm, name, SERVICE_QUERY_STATUS);
                return svc != IntPtr.Zero;
            }
            finally
            {
                if (svc != IntPtr.Zero) CloseServiceHandle(svc);
                CloseServiceHandle(scm);
            }
        }

        public static bool IsRunning(string name)
        {
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero) return false;
            IntPtr svc = IntPtr.Zero;
            try
            {
                svc = OpenService(scm, name, SERVICE_QUERY_STATUS);
                if (svc == IntPtr.Zero) return false;
                SERVICE_STATUS st = new SERVICE_STATUS();
                if (!QueryServiceStatus(svc, ref st)) return false;
                return st.dwCurrentState == SERVICE_RUNNING;
            }
            finally
            {
                if (svc != IntPtr.Zero) CloseServiceHandle(svc);
                CloseServiceHandle(scm);
            }
        }

        public static bool SetStart(string name, uint start, out string error)
        {
            error = null;
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }
            IntPtr svc = IntPtr.Zero;
            try
            {
                svc = OpenService(scm, name, SERVICE_CHANGE_CONFIG | SERVICE_QUERY_STATUS);
                if (svc == IntPtr.Zero)
                {
                    error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return false;
                }
                if (!ChangeServiceConfig(svc, SERVICE_NO_CHANGE, start, SERVICE_NO_CHANGE,
                        null, null, IntPtr.Zero, null, null, null, null))
                {
                    error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    return false;
                }
                return true;
            }
            finally
            {
                if (svc != IntPtr.Zero) CloseServiceHandle(svc);
                CloseServiceHandle(scm);
            }
        }

        public static bool Start(string name, out string error, out int errorCode, int timeoutMs)
        {
            error = null;
            errorCode = 0;
            IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
            if (scm == IntPtr.Zero)
            {
                errorCode = Marshal.GetLastWin32Error();
                error = new Win32Exception(errorCode).Message;
                return false;
            }
            IntPtr svc = IntPtr.Zero;
            try
            {
                svc = OpenService(scm, name, SERVICE_START | SERVICE_QUERY_STATUS);
                if (svc == IntPtr.Zero)
                {
                    errorCode = Marshal.GetLastWin32Error();
                    error = new Win32Exception(errorCode).Message;
                    return false;
                }
                if (!StartService(svc, 0, IntPtr.Zero))
                {
                    int e = Marshal.GetLastWin32Error();
                    if (e != ERROR_SERVICE_ALREADY_RUNNING)
                    {
                        errorCode = e;
                        error = new Win32Exception(e).Message;
                        return false;
                    }
                }
                int waited = 0;
                while (waited < timeoutMs)
                {
                    SERVICE_STATUS st = new SERVICE_STATUS();
                    if (QueryServiceStatus(svc, ref st) && st.dwCurrentState == SERVICE_RUNNING)
                        return true;
                    Thread.Sleep(300);
                    waited += 300;
                }
                errorCode = -1;
                error = "起動待ちがタイムアウトしました";
                return false;
            }
            finally
            {
                if (svc != IntPtr.Zero) CloseServiceHandle(svc);
                CloseServiceHandle(scm);
            }
        }
    }

    internal sealed class Diag
    {
        public readonly List<CheckItem> Items = new List<CheckItem>();
        public readonly List<string> Log = new List<string>();

        public bool Admin;
        public string OsText = "不明";
        public int BuildNumber;
        public bool IsWin11;

        public bool MpOk;
        public bool AvEnabled;
        public bool RtEnabled;
        public bool TamperOn;
        public string AmMode = "";
        public int SigAge = -1;
        public string SigVersion = "";
        public string ProductVersion = "";

        private const string PolRoot = @"SOFTWARE\Policies\Microsoft\Windows Defender";
        private const string RealRoot = @"SOFTWARE\Microsoft\Windows Defender";

        public static readonly string[] PolicyBadNames =
        {
            "DisableAntiSpyware", "DisableAntiVirus", "DisableRealtimeMonitoring",
            "DisableBehaviorMonitoring", "DisableIOAVProtection", "DisableScriptScanning",
            "DisableArchiveScanning", "DisableEmailScanning", "DisableRemovableDriveScanning",
            "DisableScanningNetworkFiles", "DisableBlockAtFirstSeen", "DisableEnhancedNotifications",
            "DisableIntrusionPreventionSystem", "DisableDnsSinkhole", "DisableCatchupFullScan",
            "DisableCatchupQuickScan", "DisableDatFiles", "DisableScheduledScan",
            "DisableOnAccessProtection", "DisableScanOnRealtimeEnable", "DisableNotifications"
        };

        public St Overall
        {
            get
            {
                if (Items.Any(i => i.State == St.Fail)) return St.Fail;
                if (Items.Any(i => i.State == St.Warn)) return St.Warn;
                return St.Ok;
            }
        }

        public int FailCount { get { return Items.Count(i => i.State == St.Fail); } }
        public int WarnCount { get { return Items.Count(i => i.State == St.Warn); } }

        private void Add(string name, St st, string detail, string fix)
        {
            Items.Add(new CheckItem(name, st, detail, fix));
        }

        public void RunAll()
        {
            Items.Clear();
            Admin = DefenderUtil.IsAdmin();
            DetectOs();
            LoadMpStatus();

            Add("OS / ビルド", St.Info, OsText, null);
            Add("実行権限", St.Info, Admin ? "管理者として実行中（修復可能）" : "標準ユーザー（診断のみ）",
                Admin ? null : "「管理者として実行して修復」を押すと UAC で昇格します");

            RunCheck("サービス", CheckServices);
            RunCheck("ドライバー", CheckDrivers);
            RunCheck("Defender ポリシー", CheckPolicies);
            RunCheck("セキュリティ センター プロバイダー", CheckWscProviders);
            RunCheck("Defender 実設定", CheckRealValues);
            RunCheck("IFEO", CheckIfeo);
            RunCheck("レジストリ ACL", CheckAcl);
            RunCheck("タンパープロテクション", CheckTamper);
            RunCheck("Defender エンジン", CheckEngine);
            RunCheck("Windows セキュリティ アプリ", CheckSecurityApp);
            RunCheck("Defender プラットフォーム", CheckPlatform);
            RunCheck("定義ファイル", CheckSignatures);
            RunCheck("dControl", CheckDControl);
            RunCheck("ファイアウォール", CheckFirewall);
        }

        private void RunCheck(string label, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                Log.Add(label + " の検査に失敗: " + ex.Message);
                Add(label, St.Warn, "検査中にエラーが発生しました: " + ex.Message,
                    "管理者権限で再実行してください");
            }
        }

        private void DetectOs()
        {
            try
            {
                using (RegistryKey k = Reg.Open(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    if (k == null) return;
                    string product = Convert.ToString(k.GetValue("ProductName", "Windows"));
                    string display = Convert.ToString(k.GetValue("DisplayVersion", ""));
                    BuildNumber = Reg.GetDword(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", 0);
                    int ubr = Reg.GetDword(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", 0);
                    IsWin11 = BuildNumber >= 22000;
                    if (IsWin11 && product.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
                        product = "Windows 11" + product.Substring("Windows 10".Length);
                    OsText = string.Format("{0}{1} (Build {2}.{3})",
                        product,
                        string.IsNullOrEmpty(display) ? "" : " " + display,
                        BuildNumber, ubr);
                }
            }
            catch (Exception ex)
            {
                Log.Add("OS 情報の取得に失敗: " + ex.Message);
            }
        }

        private void LoadMpStatus()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpComputerStatus"))
                using (ManagementObjectCollection col = searcher.Get())
                {
                    foreach (ManagementObject o in col)
                    {
                        using (o)
                        {
                            AvEnabled = DefenderUtil.BoolVal(o["AntivirusEnabled"]);
                            RtEnabled = DefenderUtil.BoolVal(o["RealTimeProtectionEnabled"]);
                            TamperOn = DefenderUtil.BoolVal(o["IsTamperProtected"]);
                            AmMode = Convert.ToString(o["AMRunningMode"]);
                            ProductVersion = Convert.ToString(o["AMProductVersion"]);
                            SigVersion = Convert.ToString(o["AntivirusSignatureVersion"]);
                            object age = o["AntivirusSignatureAge"];
                            if (age != null)
                            {
                                int a;
                                if (int.TryParse(age.ToString(), out a)) SigAge = a;
                            }
                            MpOk = true;
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Add("Defender WMI の取得に失敗: " + ex.Message);
                MpOk = false;
            }
        }

        private static string StartText(int s)
        {
            switch (s)
            {
                case 0: return "ブート";
                case 1: return "システム";
                case 2: return "自動";
                case 3: return "手動";
                case 4: return "無効";
                default: return "不明(" + s + ")";
            }
        }

        private void CheckServices()
        {
            CheckService("WinDefend", "Defender ウイルス対策サービス (WinDefend)", 2, true, true, true);
            CheckService("WdNisSvc", "ネットワーク検査サービス (WdNisSvc)", 3, true, false, false);
            CheckService("SecurityHealthService", "Windows セキュリティ サービス (SecurityHealthService)", 3, true, false, true);
            CheckService("wscsvc", "セキュリティ センター (wscsvc)", 2, true, true, true);
            CheckService("mpssvc", "Defender ファイアウォール (mpssvc)", 2, true, true, true);
            CheckService("Sense", "Defender for Endpoint (Sense)", 3, false, false, false);
        }

        private void CheckService(string name, string label, int want, bool wantRunning,
            bool required, bool stoppedIsFail)
        {
            if (!Svc.Exists(name))
            {
                if (required) Add(label, St.Fail, "サービスが見つかりません", "Windows の修復が必要です");
                else Add(label, St.Skip, "この OS には存在しません", null);
                return;
            }

            int start = Reg.GetDword(@"SYSTEM\CurrentControlSet\Services\" + name, "Start", -1);
            bool running = Svc.IsRunning(name);
            string state = running ? "実行中" : "停止";

            if (start == 4)
            {
                Add(label, St.Fail, "サービスが無効化されています（開始種類: 無効）",
                    "開始種類を「" + StartText(want) + "」に戻して起動します");
                return;
            }
            if (start != want)
            {
                Add(label, St.Warn, "開始種類が「" + StartText(start) + "」です（期待値: " + StartText(want) + "）",
                    "開始種類を「" + StartText(want) + "」に戻します");
                return;
            }
            if (wantRunning && !running)
            {
                Add(label, stoppedIsFail ? St.Fail : St.Warn,
                    "サービスが停止しています（開始種類: " + StartText(start) + "）", "起動します");
                return;
            }
            Add(label, St.Ok, "開始種類: " + StartText(start) + " / 状態: " + state, null);
        }

        private void CheckDrivers()
        {
            CheckDriver("WdFilter", "Defender フィルター ドライバー (WdFilter)", 0);
            CheckDriver("WdBoot", "Defender ブート ドライバー (WdBoot)", 0);
            CheckDriver("WdNisDrv", "ネットワーク検査ドライバー (WdNisDrv)", 3);
            CheckDriver("mpsdrv", "ファイアウォール ドライバー (mpsdrv)", 3);
        }

        private void CheckDriver(string name, string label, int want)
        {
            string path = @"SYSTEM\CurrentControlSet\Services\" + name;
            if (!Reg.Exists(path))
            {
                Add(label, St.Skip, "この OS には存在しません", null);
                return;
            }
            int start = Reg.GetDword(path, "Start", -1);
            if (start == 4)
                Add(label, St.Fail, "ドライバーが無効化されています", "開始種類を「" + StartText(want) + "」に戻します（再起動が必要）");
            else if (start != want)
                Add(label, St.Warn, "開始種類が「" + StartText(start) + "」です（期待値: " + StartText(want) + "）",
                    "開始種類を戻します（再起動が必要）");
            else
                Add(label, St.Ok, "開始種類: " + StartText(start), null);
        }

        private void CheckPolicies()
        {
            List<string> bad = new List<string>();
            CollectBadPolicies(PolRoot, bad);
            CollectBadPolicies(@"SOFTWARE\Policies\Microsoft\Windows Defender Security Center", bad);
            bad = bad.Distinct().ToList();

            if (bad.Count > 0)
                Add("Defender ポリシー", St.Fail, "無効化ポリシーが残っています: " + string.Join(" / ", bad),
                    "該当するポリシー値を削除します");
            else
                Add("Defender ポリシー", St.Ok, "Defender を無効化するポリシーはありません", null);
        }

        private static void CollectBadPolicies(string path, List<string> bad)
        {
            try
            {
                using (RegistryKey k = Reg.Open(path, false))
                {
                    if (k == null) return;

                    foreach (string name in k.GetValueNames())
                    {
                        int v = 0;
                        object o = k.GetValue(name);
                        if (o != null) int.TryParse(o.ToString(), out v);

                        if (Array.IndexOf(PolicyBadNames, name) >= 0 && v == 1) bad.Add(name);
                        if (name == "SpynetReporting" && v == 0)
                            bad.Add("SpynetReporting=0（クラウド保護の無効化）");
                    }

                    string[] subs = k.GetSubKeyNames();
                    foreach (string sub in subs)
                        CollectBadPolicies(path + "\\" + sub, bad);
                }
            }
            catch { }
        }

        private void CheckWscProviders()
        {
            List<string> disabled = new List<string>();
            foreach (string kind in new[] { "Av", "Fw" })
            {
                string root = @"SOFTWARE\Microsoft\Security Center\Provider\" + kind;
                string[] subs;
                try
                {
                    using (RegistryKey rk = Reg.Open(root, false))
                        subs = rk == null ? new string[0] : rk.GetSubKeyNames();
                }
                catch { continue; }

                foreach (string sub in subs)
                {
                    string path = root + "\\" + sub;
                    try
                    {
                        if (Reg.GetDword(path, "Disabled", 0) != 0) disabled.Add(kind + ": " + sub);
                    }
                    catch { }
                }
            }

            if (disabled.Count > 0)
                Add("セキュリティ センター プロバイダー", St.Warn, "無効化されています: " + string.Join(", ", disabled),
                    "Disabled を 0 に戻します");
            else
                Add("セキュリティ センター プロバイダー", St.Ok, "AV / ファイアウォール プロバイダーは有効", null);
        }

        private void CheckRealValues()
        {
            int a = Reg.GetDword(RealRoot, "DisableAntiSpyware", 0);
            int b = Reg.GetDword(RealRoot, "DisableAntiVirus", 0);
            int rt = Reg.GetDword(RealRoot + @"\Real-Time Protection", "DisableRealtimeMonitoring", 0);

            if (a == 1 || b == 1 || rt == 1)
                Add("Defender 実設定", St.Fail,
                    "無効化フラグが立っています (DisableAntiSpyware=" + a + ", DisableAntiVirus=" + b + ", DisableRealtimeMonitoring=" + rt + ")",
                    "フラグを 0 に戻します");
            else
                Add("Defender 実設定", St.Ok, "無効化フラグは立っていません", null);
        }

        private static readonly string[] IfeoExes =
        {
            "MsMpEng.exe", "MpCmdRun.exe", "NisSrv.exe", "MpUXSrv.exe",
            "SecurityHealthService.exe", "SecurityHealthSystray.exe", "SecHealthUI.exe",
            "MSASCui.exe", "MSASCuiL.exe"
        };

        private void CheckIfeo()
        {
            List<string> hits = new List<string>();
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"
            };
            foreach (string root in roots)
            {
                foreach (string exe in IfeoExes)
                {
                    string path = root + "\\" + exe;
                    using (RegistryKey k = Reg.Open(path, false))
                    {
                        if (k == null) continue;
                        object dbg = k.GetValue("Debugger");
                        if (dbg != null) hits.Add(exe + " → " + dbg);
                    }
                }
            }
            if (hits.Count > 0)
                Add("IFEO 乗っ取り", St.Fail, "デバッガーが仕込まれています: " + string.Join(" / ", hits),
                    "Debugger 値を削除します");
            else
                Add("IFEO 乗っ取り", St.Ok, "Defender 実行ファイルへの IFEO 乗っ取りはありません", null);
        }

        private static readonly string[] AclKeys =
        {
            RealRoot,
            PolRoot,
            @"SYSTEM\CurrentControlSet\Services\WinDefend",
            @"SYSTEM\CurrentControlSet\Services\WdNisSvc",
            @"SYSTEM\CurrentControlSet\Services\Sense",
            @"SYSTEM\CurrentControlSet\Services\SecurityHealthService",
            @"SYSTEM\CurrentControlSet\Services\wscsvc"
        };

        private void CheckAcl()
        {
            if (!Admin)
            {
                Add("レジストリ ACL", St.Skip, "ACL の確認には管理者権限が必要です", null);
                return;
            }

            int deny = 0;
            List<string> keys = new List<string>();
            foreach (string path in AclKeys)
            {
                try
                {
                    using (RegistryKey k = Reg.OpenReadAcl(path))
                    {
                        if (k == null) continue;
                        RegistrySecurity sec = k.GetAccessControl(AccessControlSections.Access);
                        foreach (AuthorizationRule rule in sec.GetAccessRules(true, false, typeof(NTAccount)))
                        {
                            RegistryAccessRule r = rule as RegistryAccessRule;
                            if (r != null && r.AccessControlType == AccessControlType.Deny)
                            {
                                deny++;
                                if (!keys.Contains(path)) keys.Add(path);
                            }
                        }
                    }
                }
                catch { }
            }

            if (deny > 0)
                Add("レジストリ ACL", St.Warn, "拒否 (Deny) エントリが " + deny + " 個あります: " + string.Join(", ", keys),
                    "拒否エントリを削除します");
            else
                Add("レジストリ ACL", St.Ok, "Defender 関連キーに拒否エントリはありません", null);
        }

        private void CheckTamper()
        {
            if (!MpOk)
            {
                Add("タンパープロテクション", St.Skip, "Defender エンジンが停止しているため確認できません", null);
                return;
            }
            if (TamperOn)
                Add("タンパープロテクション", St.Ok, "有効（無効化の変更はブロックされます）", null);
            else
                Add("タンパープロテクション", St.Warn, "無効になっています",
                    "Windows セキュリティ > ウイルスと脅威の防止 > 設定の管理 から有効にしてください（プログラムからは変更できません）");
        }

        private void CheckEngine()
        {
            if (!MpOk)
            {
                Add("Defender エンジン", St.Fail, "Defender の WMI 状態を取得できません（サービス停止の可能性）",
                    "サービスを復元してから再診断します");
                return;
            }

            string mode = string.IsNullOrEmpty(AmMode) ? "不明" : AmMode;
            if (string.Equals(mode, "Passive", StringComparison.OrdinalIgnoreCase))
            {
                Add("Defender エンジン", St.Info, "パッシブ モード（他のウイルス対策ソフトが有効）", null);
                return;
            }

            if (AvEnabled && RtEnabled)
                Add("Defender エンジン", St.Ok,
                    "ウイルス対策: 有効 / リアルタイム保護: 有効 / モード: " + mode + " / " + ProductVersion,
                    null);
            else
                Add("Defender エンジン", St.Fail,
                    "ウイルス対策: " + (AvEnabled ? "有効" : "無効") + " / リアルタイム保護: " + (RtEnabled ? "有効" : "無効") + " / モード: " + mode,
                    "設定を復元します");
        }

        private void CheckSecurityApp()
        {
            if (BuildNumber > 0 && BuildNumber < 16299)
            {
                Add("Windows セキュリティ アプリ", St.Skip, "この OS では SecHealthUI を使用しません", null);
                return;
            }

            bool contract = false;
            try
            {
                using (RegistryKey root = Registry.ClassesRoot.OpenSubKey(
                    @"Extensions\ContractId\Windows.Protocol\PackageId", false))
                {
                    if (root != null)
                        contract = root.GetSubKeyNames().Any(n => n.IndexOf("SecHealthUI", StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }
            catch { }

            bool installed = false;
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(pf))
                {
                    string wa = Path.Combine(pf, "WindowsApps");
                    if (Directory.Exists(wa))
                        installed = Directory.GetDirectories(wa, "microsoft.sechealthui_*").Length > 0;
                }
            }
            catch { }

            if (contract)
                Add("Windows セキュリティ アプリ", St.Ok, "登録済み（windowsdefender: プロトコル有効）", null);
            else if (installed)
                Add("Windows セキュリティ アプリ", St.Warn, "パッケージはありますが登録が壊れています",
                    "アプリを再登録します");
            else
                Add("Windows セキュリティ アプリ", St.Warn, "Windows セキュリティ アプリが見つかりません",
                    "アプリの再登録を試みます");
        }

        private void CheckPlatform()
        {
            string dir = DefenderUtil.PlatformDir;
            if (dir == null)
            {
                Add("Defender プラットフォーム", St.Fail, "MsMpEng.exe が見つかりません",
                    "Defender プラットフォームの修復が必要です");
                return;
            }

            string exe = Path.Combine(dir, "MsMpEng.exe");
            string ver = "";
            try { ver = FileVersionInfo.GetVersionInfo(exe).FileVersion; } catch { }
            if (File.Exists(exe))
                Add("Defender プラットフォーム", St.Ok, "MsMpEng.exe " + ver + "（" + Path.GetFileName(dir) + "）", null);
            else
                Add("Defender プラットフォーム", St.Fail, "MsMpEng.exe がありません", "プラットフォームの再インストールが必要です");
        }

        private void CheckSignatures()
        {
            if (!MpOk)
            {
                Add("定義ファイル", St.Skip, "Defender エンジンが停止しているため確認できません", null);
                return;
            }
            if (SigAge < 0)
            {
                Add("定義ファイル", St.Info, "バージョン: " + SigVersion, null);
                return;
            }
            if (SigAge > 7)
                Add("定義ファイル", St.Warn, "定義が " + SigAge + " 日古いです（バージョン: " + SigVersion + "）",
                    "定義を更新します");
            else
                Add("定義ファイル", St.Ok, "最新です（" + SigAge + " 日前 / バージョン: " + SigVersion + "）", null);
        }

        private void CheckDControl()
        {
            List<string> strong = new List<string>();
            List<string> weak = new List<string>();

            try
            {
                string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string[] dirs = Directory.GetDirectories(dl, "dControl*", SearchOption.TopDirectoryOnly);
                foreach (string d in dirs) weak.Add(d);
            }
            catch { }

            try
            {
                if (Process.GetProcessesByName("dControl").Length > 0) strong.Add("dControl プロセスが実行中");
            }
            catch { }

            if (Admin)
            {
                foreach (string sub in new[] { "Paths", "Processes", "Extensions" })
                {
                    string path = RealRoot + @"\Exclusions\" + sub;
                    foreach (string v in Reg.ValueNames(path))
                    {
                        if (v.IndexOf("dcontrol", StringComparison.OrdinalIgnoreCase) >= 0)
                            strong.Add("除外設定: " + v);
                    }
                }
            }

            if (strong.Count > 0)
                Add("dControl の残骸", St.Warn, string.Join(" / ", strong),
                    "除外設定は削除します（フォルダーは手動で削除してください）");
            else if (weak.Count > 0)
                Add("dControl の残骸", St.Info,
                    "dControl のフォルダーが残っています: " + string.Join(" / ", weak) + "（Defender の動作には影響しません）",
                    "不要なら手動で削除してください");
            else
                Add("dControl の残骸", St.Ok, "dControl の除外設定・常駐プロセスは見つかりません", null);
        }

        private void CheckFirewall()
        {
            string basePath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
            string[] profiles = { "DomainProfile", "StandardProfile", "PublicProfile" };
            List<string> off = new List<string>();
            foreach (string p in profiles)
            {
                if (Reg.GetDword(basePath + "\\" + p, "EnableFirewall", 1) == 0) off.Add(p);
            }

            if (off.Count > 0)
                Add("ファイアウォール", St.Warn, "無効なプロファイル: " + string.Join(", ", off),
                    "ファイアウォールを有効化します");
            else
                Add("ファイアウォール", St.Ok, "すべてのプロファイルで有効", null);
        }

        public string Report()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Defender 復元ツール 診断レポート");
            sb.AppendLine("生成日時: " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"));
            sb.AppendLine("OS: " + OsText);
            sb.AppendLine("権限: " + (Admin ? "管理者" : "標準ユーザー"));
            sb.AppendLine("総合判定: " + (Overall == St.Fail ? "修復が必要" : Overall == St.Warn ? "確認が必要" : "正常"));
            sb.AppendLine("要修復: " + FailCount + " 件 / 注意: " + WarnCount + " 件");
            sb.AppendLine();
            sb.AppendLine("--- チェック結果 ---");
            foreach (CheckItem it in Items)
            {
                sb.AppendLine("[" + it.StateText + "] " + it.Name + ": " + it.Detail);
                if (!string.IsNullOrEmpty(it.Fix)) sb.AppendLine("        対処: " + it.Fix);
            }

            sb.AppendLine();
            sb.AppendLine("--- サービス構成 ---");
            foreach (string n in new[] { "WinDefend", "WdNisSvc", "SecurityHealthService", "wscsvc", "mpssvc", "Sense" })
            {
                try
                {
                    if (!Svc.Exists(n)) { sb.AppendLine(n + ": なし"); continue; }
                    int start = Reg.GetDword(@"SYSTEM\CurrentControlSet\Services\" + n, "Start", -1);
                    string deps = "";
                    using (RegistryKey k = Reg.Open(@"SYSTEM\CurrentControlSet\Services\" + n, false))
                    {
                        if (k != null)
                        {
                            object dep = k.GetValue("DependOnService", "");
                            string[] arr = dep as string[];
                            deps = arr != null ? string.Join(",", arr) : Convert.ToString(dep);
                        }
                    }
                    sb.AppendLine(string.Format("{0}: Start={1}({2}) / 実行={3} / 依存={4}",
                        n, start, StartText(start), Svc.IsRunning(n), deps));
                }
                catch (Exception ex)
                {
                    sb.AppendLine(n + ": 取得失敗 " + ex.Message);
                }
            }

            if (Log.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- 内部ログ ---");
                foreach (string l in Log) sb.AppendLine(l);
            }
            return sb.ToString();
        }
    }

    internal sealed class Repairer
    {
        private readonly Diag D;
        private readonly Action<string> Sink;
        public readonly List<string> Log = new List<string>();
        public bool RebootNeeded;

        public Repairer(Diag diag, Action<string> sink)
        {
            D = diag;
            Sink = sink;
        }

        private void L(string s)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
            Log.Add(line);
            if (Sink != null) Sink(line);
        }

        private void Step(string name, Action action)
        {
            L("■ " + name);
            try
            {
                action();
                L("  → 完了");
            }
            catch (Exception ex)
            {
                L("  → 失敗: " + ex.Message);
            }
        }

        private const string PolRoot = @"SOFTWARE\Policies\Microsoft\Windows Defender";
        private const string RealRoot = @"SOFTWARE\Microsoft\Windows Defender";

        public void Run(bool restorePoint, bool restoreDefaults, bool updateSigs)
        {
            if (restorePoint) Step("システムの復元ポイント作成", CreateRestorePoint);
            Step("無効化ポリシーの削除", CleanPolicies);
            Step("Defender 実設定の復元", CleanRealValues);
            Step("レジストリ ACL の拒否エントリ除去", FixAcl);
            Step("セキュリティ センター プロバイダーの復元", FixSecurityCenterProviders);
            Step("サービスの開始構成の復元", FixServices);
            Step("ドライバーの開始構成の復元", FixDrivers);
            Step("IFEO 乗っ取りの除去", FixIfeo);
            Step("dControl 除外設定の除去", FixExclusions);
            Step("Windows セキュリティ アプリの確認", EnsureSecurityApp);
            Step("ファイアウォールの復元", FixFirewall);
            if (restoreDefaults) Step("Defender 設定の初期化 (RestoreDefaults)", RunRestoreDefaults);
            if (updateSigs) Step("定義ファイルの更新", UpdateSignatures);
            Step("サービスの起動", StartServices);
        }

        private void CleanPolicies()
        {
            int n = 0;
            n += CleanPolicyTree(PolRoot);
            n += CleanPolicyTree(@"SOFTWARE\Policies\Microsoft\Windows Defender Security Center");
            L("  削除したポリシー値: " + n + " 個");
        }

        private int CleanPolicyTree(string path)
        {
            int n = 0;
            try
            {
                using (RegistryKey k = Reg.Open(path, true))
                {
                    if (k == null) return 0;

                    foreach (string name in k.GetValueNames())
                    {
                        bool bad = Array.IndexOf(Diag.PolicyBadNames, name) >= 0;
                        if (name == "SpynetReporting")
                        {
                            int v;
                            object o = k.GetValue(name);
                            bad = o != null && int.TryParse(o.ToString(), out v) && v == 0;
                        }
                        if (!bad) continue;

                        k.DeleteValue(name, false);
                        n++;
                        L("  削除: " + path + "\\" + name);
                    }

                    foreach (string sub in k.GetSubKeyNames())
                        n += CleanPolicyTree(path + "\\" + sub);
                }
            }
            catch (Exception ex)
            {
                L("  " + path + ": " + ex.Message);
            }
            return n;
        }

        private void CleanRealValues()
        {
            if (Reg.Exists(RealRoot))
            {
                Reg.SetDword(RealRoot, "DisableAntiSpyware", 0);
                Reg.SetDword(RealRoot, "DisableAntiVirus", 0);
                L("  DisableAntiSpyware=0 / DisableAntiVirus=0 に設定");
            }
            if (Reg.Exists(RealRoot + @"\Real-Time Protection"))
            {
                Reg.SetDword(RealRoot + @"\Real-Time Protection", "DisableRealtimeMonitoring", 0);
                L("  DisableRealtimeMonitoring=0 に設定");
            }
            if (Reg.GetDword(RealRoot + @"\Features", "TamperProtection", 0) > 0)
                L("  タンパープロテクションは有効のまま維持します（無効化しません）");
        }

        private void FixSecurityCenterProviders()
        {
            int n = 0;
            foreach (string kind in new[] { "Av", "Fw" })
            {
                string root = @"SOFTWARE\Microsoft\Security Center\Provider\" + kind;
                string[] subs;
                using (RegistryKey rk = Reg.Open(root, false))
                    subs = rk == null ? new string[0] : rk.GetSubKeyNames();

                foreach (string sub in subs)
                {
                    string path = root + "\\" + sub;
                    if (Reg.GetDword(path, "Disabled", 0) == 0) continue;
                    if (Reg.SetDword(path, "Disabled", 0))
                    {
                        n++;
                        L("  " + kind + " プロバイダー " + sub + " を有効化");
                    }
                }
            }
            if (n == 0) L("  無効化されたプロバイダーはありません");
        }

        private void EnsureWinDefendImagePath()
        {
            string path = @"SYSTEM\CurrentControlSet\Services\WinDefend";
            string current = "";
            using (RegistryKey k = Reg.Open(path, false))
                if (k != null) current = Convert.ToString(k.GetValue("ImagePath", ""));

            string unquoted = current.Trim('"');
            if (!string.IsNullOrEmpty(unquoted) && File.Exists(unquoted)) return;

            string exe = DefenderUtil.MsMpEng;
            if (exe == null || !File.Exists(exe))
            {
                L("  WinDefend の ImagePath を修復できません（MsMpEng.exe が見つかりません）");
                return;
            }

            using (RegistryKey k = Reg.Open(path, true))
            {
                if (k == null) return;
                k.SetValue("ImagePath", "\"" + exe + "\"", RegistryValueKind.String);
                L("  WinDefend の ImagePath を修復: " + exe);
            }
        }

        private void FixServices()
        {
            EnsureWinDefendImagePath();

            object[,] defs =
            {
                { "WinDefend", 2u },
                { "WdNisSvc", 3u },
                { "SecurityHealthService", 3u },
                { "wscsvc", 2u },
                { "mpssvc", 2u },
                { "Sense", 3u }
            };

            for (int i = 0; i < defs.GetLength(0); i++)
            {
                string name = (string)defs[i, 0];
                uint want = (uint)defs[i, 1];
                if (!Svc.Exists(name)) continue;

                int cur = Reg.GetDword(@"SYSTEM\CurrentControlSet\Services\" + name, "Start", -1);
                if (cur == want) continue;

                string err;
                if (Svc.SetStart(name, want, out err))
                {
                    L("  " + name + ": 開始種類 " + cur + " → " + want);
                }
                else if (Reg.SetDword(@"SYSTEM\CurrentControlSet\Services\" + name, "Start", (int)want))
                {
                    L("  " + name + ": 開始種類 " + cur + " → " + want + "（レジストリ経由 / 再起動で反映）");
                    RebootNeeded = true;
                }
                else
                {
                    L("  " + name + ": 変更できません (" + err + ")");
                }
            }
        }

        private void FixDrivers()
        {
            object[,] defs =
            {
                { "WdFilter", 0 },
                { "WdBoot", 0 },
                { "WdNisDrv", 3 },
                { "mpsdrv", 3 }
            };

            for (int i = 0; i < defs.GetLength(0); i++)
            {
                string name = (string)defs[i, 0];
                int want = (int)defs[i, 1];
                string path = @"SYSTEM\CurrentControlSet\Services\" + name;
                if (!Reg.Exists(path)) continue;

                int cur = Reg.GetDword(path, "Start", -1);
                if (cur == want) continue;

                if (Reg.SetDword(path, "Start", want))
                {
                    L("  " + name + ": Start " + cur + " → " + want + "（再起動で反映）");
                    RebootNeeded = true;
                }
                else L("  " + name + ": 変更できません");
            }
        }

        private void FixIfeo()
        {
            string[] roots =
            {
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"
            };
            string[] exes =
            {
                "MsMpEng.exe", "MpCmdRun.exe", "NisSrv.exe", "MpUXSrv.exe",
                "SecurityHealthService.exe", "SecurityHealthSystray.exe", "SecHealthUI.exe",
                "MSASCui.exe", "MSASCuiL.exe"
            };

            int n = 0;
            foreach (string root in roots)
            {
                foreach (string exe in exes)
                {
                    string path = root + "\\" + exe;
                    try
                    {
                        if (Reg.DeleteValue(path, "Debugger"))
                        {
                            n++;
                            L("  削除: " + path + "\\Debugger");
                        }
                    }
                    catch (Exception ex)
                    {
                        L("  削除できません: " + path + " (" + ex.Message + ")");
                    }
                }
            }
            if (n == 0) L("  対象はありませんでした");
        }

        private void FixAcl()
        {
            string[] keys =
            {
                @"SOFTWARE\Microsoft\Windows Defender",
                @"SOFTWARE\Policies\Microsoft\Windows Defender",
                @"SYSTEM\CurrentControlSet\Services\WinDefend",
                @"SYSTEM\CurrentControlSet\Services\WdNisSvc",
                @"SYSTEM\CurrentControlSet\Services\Sense",
                @"SYSTEM\CurrentControlSet\Services\SecurityHealthService",
                @"SYSTEM\CurrentControlSet\Services\wscsvc"
            };

            int removed = 0;
            foreach (string path in keys)
            {
                try
                {
                    using (RegistryKey k = Reg.OpenWriteAcl(path))
                    {
                        if (k == null) continue;
                        RegistrySecurity sec = k.GetAccessControl(AccessControlSections.Access);
                        List<RegistryAccessRule> deny = new List<RegistryAccessRule>();
                        foreach (AuthorizationRule rule in sec.GetAccessRules(true, false, typeof(NTAccount)))
                        {
                            RegistryAccessRule r = rule as RegistryAccessRule;
                            if (r != null && r.AccessControlType == AccessControlType.Deny) deny.Add(r);
                        }
                        if (deny.Count == 0) continue;

                        foreach (RegistryAccessRule r in deny)
                        {
                            sec.RemoveAccessRule(r);
                            removed++;
                            L("  削除: " + path + " の拒否 " + r.IdentityReference);
                        }
                        k.SetAccessControl(sec);
                    }
                }
                catch (Exception ex)
                {
                    L("  " + path + ": " + ex.Message);
                }
            }
            if (removed == 0) L("  拒否エントリはありませんでした");
        }

        private void FixExclusions()
        {
            int n = 0;
            foreach (string sub in new[] { "Paths", "Processes", "Extensions" })
            {
                string path = RealRoot + @"\Exclusions\" + sub;
                foreach (string v in Reg.ValueNames(path))
                {
                    if (v.IndexOf("dcontrol", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        using (RegistryKey k = Reg.Open(path, true))
                        {
                            if (k != null)
                            {
                                k.DeleteValue(v, false);
                                n++;
                                L("  削除: " + sub + " の除外 " + v);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        L("  削除できません: " + v + " (" + ex.Message + ")");
                    }
                }
            }
            if (n == 0) L("  dControl の除外設定はありませんでした");
        }

        private void EnsureSecurityApp()
        {
            bool contract = false;
            try
            {
                using (RegistryKey root = Registry.ClassesRoot.OpenSubKey(
                    @"Extensions\ContractId\Windows.Protocol\PackageId", false))
                {
                    if (root != null)
                        contract = root.GetSubKeyNames().Any(n => n.IndexOf("SecHealthUI", StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }
            catch { }

            if (contract)
            {
                L("  登録済みのため再登録は不要です");
                return;
            }

            string script =
                "$ErrorActionPreference='Stop'; " +
                "$pkgs=@(Get-AppxPackage -AllUsers | Where-Object { $_.Name -like '*SecHealthUI*' }); " +
                "if($pkgs.Count -eq 0){ Write-Output 'PACKAGE_NOT_FOUND'; exit 2 }; " +
                "foreach($p in $pkgs){ Add-AppxPackage -DisableDevelopmentMode -Register (Join-Path $p.InstallLocation 'AppxManifest.xml') -ErrorAction Stop }; " +
                "Write-Output 'REGISTERED'; exit 0";

            int code;
            string output = DefenderUtil.PowerShell(script, 120000, out code);
            L("  PowerShell 終了コード: " + code);
            if (!string.IsNullOrEmpty(output)) L("  " + output.Replace("\r", "").Replace("\n", " / "));
        }

        private void FixFirewall()
        {
            string basePath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
            foreach (string p in new[] { "DomainProfile", "StandardProfile", "PublicProfile" })
            {
                string path = basePath + "\\" + p;
                if (!Reg.Exists(path)) continue;
                if (Reg.GetDword(path, "EnableFirewall", 1) == 0)
                {
                    if (Reg.SetDword(path, "EnableFirewall", 1))
                        L("  " + p + ": ファイアウォールを有効化");
                }
            }
        }

        private void CreateRestorePoint()
        {
            int code;
            string output = DefenderUtil.PowerShell(
                "Checkpoint-Computer -Description 'DefenderRestore' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; Write-Output 'OK'",
                180000, out code);
            L("  終了コード: " + code + (string.IsNullOrEmpty(output) ? "" : " / " + output));
        }

        private void RunRestoreDefaults() { RunMpCmd("-RestoreDefaults", 180000); }
        private void UpdateSignatures() { RunMpCmd("-SignatureUpdate", 300000); }

        private void RunMpCmd(string args, int timeout)
        {
            string exe = DefenderUtil.MpCmdRun;
            if (exe == null || !File.Exists(exe))
            {
                L("  MpCmdRun.exe が見つかりません");
                return;
            }
            int code;
            string output = DefenderUtil.RunProcess(exe, args, timeout, out code);
            L("  " + Path.GetFileName(exe) + " " + args + " → 終了コード " + code);
            if (!string.IsNullOrEmpty(output)) L("  " + output.Replace("\r", "").Replace("\n", " / "));
        }

        private void StartServices()
        {
            string[] order = { "wscsvc", "WinDefend", "WdNisSvc", "SecurityHealthService", "mpssvc" };
            foreach (string name in order)
            {
                if (!Svc.Exists(name)) continue;
                if (Svc.IsRunning(name))
                {
                    L("  " + name + ": 既に実行中");
                    continue;
                }
                string err;
                int code;
                if (Svc.Start(name, out err, out code, 25000))
                {
                    L("  " + name + ": 起動しました");
                }
                else
                {
                    L("  " + name + ": 起動できません (" + err + " / エラーコード: " + code + ")");
                    if (code == 1058)
                    {
                        L("    → 開始種類は変更済みでも、SCM が旧状態を保持している場合があります。");
                        L("    → Windows を再起動してから、もう一度このツールを実行してください。");
                        RebootNeeded = true;
                    }
                    if (name == "wscsvc" || name == "SecurityHealthService" || code == 1058)
                        LogServiceConfig(name);
                }
            }
        }

        private void LogServiceConfig(string name)
        {
            int code;
            string output = DefenderUtil.RunProcess("sc.exe", "qc " + name, 15000, out code);
            if (!string.IsNullOrEmpty(output))
                L("  構成: " + output.Replace("\r", "").Replace("\n", " / "));
        }
    }
}
