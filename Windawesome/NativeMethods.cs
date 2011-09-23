using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Windawesome
{
using DWORD = UInt32;
using HANDLE = IntPtr;
using HDWP = IntPtr;
using HHOOK = IntPtr;
using HICON = IntPtr;
using HINSTANCE = IntPtr;
using HMENU = IntPtr;
using HMODULE = IntPtr;
using HMONITOR = IntPtr;
using HSID = IntPtr;
using HWINEVENTHOOK = IntPtr;
using HWND = IntPtr;
using LONG = Int32;
using LPARAM = IntPtr; // INT_PTR
using LPVOID = IntPtr;
using LRESULT = IntPtr; // LONG_PTR
using PVOID = IntPtr;
using SIZE_T = IntPtr;
using UINT = UInt32;
using UINT_PTR = UIntPtr;
using WPARAM = UIntPtr; // UINT_PTR

#if !DEBUG
	[System.Security.SuppressUnmanagedCodeSecurity]
#endif
	public static class NativeMethods
	{
		static NativeMethods()
		{
			if (Environment.Is64BitProcess)
			{
				SetWindowStyleLongPtr = (hWnd, style) => SetWindowLongPtr64(hWnd, GWL_STYLE, style);
				SetWindowExStyleLongPtr = (hWnd, exStyle) => SetWindowLongPtr64(hWnd, GWL_EXSTYLE, exStyle);
				GetWindowStyleLongPtr = hWnd => GetWindowLongPtr64WS(hWnd, GWL_STYLE);
				GetWindowExStyleLongPtr = hWnd => GetWindowLongPtr64WS_EX(hWnd, GWL_EXSTYLE);
				GetClassLongPtr = GetClassLongPtr64;
				UnsubclassWindow = UnsubclassWindow64;
				RunApplicationNonElevated = RunApplicationNonElevated64;
				RegisterSystemTrayHook = RegisterSystemTrayHook64;
				UnregisterSystemTrayHook = UnregisterSystemTrayHook64;
			}
			else
			{
				SetWindowStyleLongPtr = (hWnd, style) => SetWindowLong32(hWnd, GWL_STYLE, style);
				SetWindowExStyleLongPtr = (hWnd, exStyle) => SetWindowLong32(hWnd, GWL_EXSTYLE, exStyle);
				GetWindowStyleLongPtr = hWnd => GetWindowLong32WS(hWnd, GWL_STYLE);
				GetWindowExStyleLongPtr = hWnd => GetWindowLong32WS_EX(hWnd, GWL_EXSTYLE);
				GetClassLongPtr = GetClassLong32;
				UnsubclassWindow = UnsubclassWindow32;
				RunApplicationNonElevated = RunApplicationNonElevated32;
				if (Environment.Is64BitOperatingSystem)
				{
					RegisterSystemTrayHook = _ => false;
					UnregisterSystemTrayHook = () => true;
				}
				else
				{
					RegisterSystemTrayHook = RegisterSystemTrayHook32;
					UnregisterSystemTrayHook = UnregisterSystemTrayHook32;
				}
			}

			NONCLIENTMETRICSSize = Marshal.SizeOf(typeof(NONCLIENTMETRICS)) - (Windawesome.isAtLeastVista ? 0 : 4);
		}

		// hooks stuff

		#region SetWindowsHookEx/CallNextHookEx/UnhookWindowsHookEx

		public const int WH_KEYBOARD_LL = 13;

		public static readonly WPARAM WM_KEYDOWN = (WPARAM) 0x100;
		public static readonly WPARAM WM_SYSKEYDOWN = (WPARAM) 0x104;

		public delegate LRESULT HookProc(int code, WPARAM wParam, LPARAM lParam);

		[DllImport("user32.dll")]
		public static extern HHOOK SetWindowsHookEx(int hookType, [MarshalAs(UnmanagedType.FunctionPtr)] HookProc lpfn, HINSTANCE hMod, DWORD dwThreadId);

		[DllImport("user32.dll")]
		public static extern LRESULT CallNextHookEx([Optional] HHOOK hhk, int nCode, WPARAM wParam, LPARAM lParam);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool UnhookWindowsHookEx(HHOOK hhk);

		#endregion

		#region RegisterShellHookWindow/DeregisterShellHookWindow

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool RegisterShellHookWindow(HWND hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool DeregisterShellHookWindow(HWND hWnd);

		public static readonly UINT WM_SHELLHOOKMESSAGE = RegisterWindowMessage("SHELLHOOK");

		public enum ShellEvents : uint
		{
			HSHELL_WINDOWCREATED = 1,
			HSHELL_WINDOWDESTROYED = 2,
			HSHELL_ACTIVATESHELLWINDOW = 3,
			HSHELL_WINDOWACTIVATED = 4,
			HSHELL_GETMINRECT = 5,
			HSHELL_REDRAW = 6,
			HSHELL_TASKMAN = 7,
			HSHELL_LANGUAGE = 8,
			HSHELL_SYSMENU = 9,
			HSHELL_ENDTASK = 10,
			HSHELL_ACCESSIBILITYSTATE = 11,
			HSHELL_APPCOMMAND = 12,
			HSHELL_WINDOWREPLACED = 13,
			HSHELL_WINDOWREPLACING = 14,
			HSHELL_HIGHBIT = 0x8000,
			HSHELL_FLASH = (HSHELL_REDRAW | HSHELL_HIGHBIT),
			HSHELL_RUDEAPPACTIVATED = (HSHELL_WINDOWACTIVATED | HSHELL_HIGHBIT)
		}

		#endregion

		#region SetWinEventHook/UnhookWinEvent

		public delegate void WinEventDelegate(HWINEVENTHOOK hWinEventHook, EVENT eventType,
			HWND hwnd, LONG idObject, LONG idChild, DWORD dwEventThread, DWORD dwmsEventTime);

		[DllImport("user32.dll")]
		public static extern HWINEVENTHOOK SetWinEventHook(EVENT eventMin, EVENT eventMax, HMODULE hmodWinEventProc,
			[MarshalAs(UnmanagedType.FunctionPtr)] WinEventDelegate lpfnWinEventProc, DWORD idProcess, DWORD idThread, WINEVENT dwFlags);

		public enum EVENT : uint
		{
			EVENT_OBJECT_DESTROY = 0x00008001,
			EVENT_OBJECT_SHOW = 0x00008002,
			EVENT_OBJECT_HIDE = 0x00008003,

			EVENT_SYSTEM_MINIMIZESTART = 0x16,
			EVENT_SYSTEM_MINIMIZEEND = 0x17
		}

		public const LONG OBJID_WINDOW = 0x00000000;
		public const LONG CHILDID_SELF = 0;

		[Flags]
		public enum WINEVENT : uint
		{
			WINEVENT_OUTOFCONTEXT = 0x0000,
			WINEVENT_SKIPOWNTHREAD = 0x0001
		}

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool UnhookWinEvent(HWINEVENTHOOK hWinEventHook);

		#endregion

		// messages stuff

		#region SendNotifyMessage/ReplyMessage/PostMessage/SendMessageTimeout

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SendNotifyMessage(HWND hWnd, UINT Msg, WPARAM wParam, LPARAM lParam);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ReplyMessage(LRESULT lResult);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool PostMessage([Optional] HWND hWnd, UINT Msg, WPARAM wParam, LPARAM lParam);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool PostMessage([Optional] HWND hWnd, int Msg, IntPtr wParam, LPARAM lParam);

		[DllImport("User32.dll")]
		public static extern LRESULT SendMessageTimeout(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam, SMTO fuFlags, UINT uTimeout, [Optional, Out] out IntPtr lpdwResult);

		[DllImport("User32.dll", SetLastError = true)]
		public static extern LRESULT SendMessageTimeout(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam, SMTO fuFlags, UINT uTimeout, [Optional, Out] IntPtr lpdwResult);

		[Flags]
		public enum SMTO : uint
		{
			SMTO_NORMAL = 0x0,
			SMTO_BLOCK = 0x1,
			SMTO_ABORTIFHUNG = 0x2,
			SMTO_NOTIMEOUTIFNOTHUNG = 0x8,
			SMTO_ERRORONEXIT = 0x0020
		}

		public static readonly HWND HWND_BROADCAST = (HWND) 0xFFFF;
		public const int WM_MOUSEACTIVATE = 0x0021;
		public static readonly IntPtr MA_NOACTIVATE = (IntPtr) 0x0003;
		public const UINT WM_SYSCOMMAND = 0x0112;
		public static readonly LPARAM WM_LBUTTONDBLCLK = (LPARAM) 0x0203;
		public static readonly LPARAM WM_RBUTTONDBLCLK = (LPARAM) 0x0206;
		public static readonly LPARAM WM_LBUTTONDOWN = (LPARAM) 0x0201;
		public static readonly LPARAM WM_LBUTTONUP = (LPARAM) 0x0202;
		public static readonly LPARAM WM_RBUTTONDOWN = (LPARAM) 0x204;
		public static readonly LPARAM WM_RBUTTONUP = (LPARAM) 0x205;
		public const UINT WM_GETICON = 0x007f;
		public const UINT WM_QUERYDRAGICON = 0x0037;
		public const UINT WM_NULL = 0x0;
		public const int ERROR_TIMEOUT = 1460;

		public static readonly WPARAM SC_MINIMIZE = (WPARAM) 0xF020;
		public static readonly WPARAM SC_MAXIMIZE = (WPARAM) 0xF030;
		public static readonly WPARAM SC_RESTORE = (WPARAM) 0xF120;
		public static readonly WPARAM SC_CLOSE = (WPARAM) 0xF060;

		public static readonly WPARAM ICON_SMALL = UIntPtr.Zero;

		#endregion

		#region SHAppBarMessage

		private static readonly int APPBARDATASize = Marshal.SizeOf(typeof(APPBARDATA));

		[StructLayout(LayoutKind.Sequential)]
		public struct APPBARDATA
		{
			public int cbSize;
			public HWND hWnd;
			public UINT uCallbackMessage;
			public ABE uEdge;
			public RECT rc;
			public LPARAM lParam;

			public APPBARDATA(HWND hWnd, UINT uCallbackMessage = default(UINT), ABE uEdge = default(ABE), RECT rc = default(RECT), LPARAM lParam = default(LPARAM))
			{
				cbSize = APPBARDATASize;
				this.hWnd = hWnd;
				this.uCallbackMessage = uCallbackMessage;
				this.uEdge = uEdge;
				this.rc = rc;
				this.lParam = lParam;
			}
		}

		public enum ABM : uint
		{
			ABM_NEW = 0,
			ABM_REMOVE = 1,
			ABM_QUERYPOS = 2,
			ABM_SETPOS = 3,
			ABM_GETSTATE = 4,
			ABM_GETTASKBARPOS = 5,
			ABM_ACTIVATE = 6,
			ABM_GETAUTOHIDEBAR = 7,
			ABM_SETAUTOHIDEBAR = 8,
			ABM_WINDOWPOSCHANGED = 9,
			ABM_SETSTATE = 10
		}

		public enum ABN : uint
		{
			ABN_STATECHANGE = 0,
			ABN_POSCHANGED,
			ABN_FULLSCREENAPP,
			ABN_WINDOWARRANGE
		}

		public enum ABE : uint
		{
			ABE_LEFT = 0,
			ABE_TOP,
			ABE_RIGHT,
			ABE_BOTTOM
		}

		[Flags]
		public enum ABS : uint
		{
			ABS_AUTOHIDE = 1,
			ABS_ALWAYSONTOP = 2
		}

		[DllImport("shell32.dll")]
		public static extern UINT_PTR SHAppBarMessage(ABM dwMessage, [In, Out] ref APPBARDATA pData);

		#endregion

		[DllImport("User32.dll", CharSet = CharSet.Auto)]
		public static extern UINT RegisterWindowMessage([MarshalAs(UnmanagedType.LPTStr)] string msg);

		#region ChangeWindowMessageFilter/ChangeWindowMessageFilterEx

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ChangeWindowMessageFilter(UINT message, MSGFLT dwFlag);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ChangeWindowMessageFilterEx(HWND hWnd, UINT message, MSGFLTEx action, [Optional, In, Out] IntPtr str);

		public enum MSGFLT : uint
		{
			MSGFLT_ADD = 1,
			MSGFLT_REMOVE = 2 
		}

		public enum MSGFLTEx : uint
		{
			MSGFLT_ALLOW = 1,
			MSGFLT_RESET = 0
		}

		#endregion

		public const UINT WM_USER = 0x0400;

		// window stuff

		public static readonly HWND HWND_MESSAGE = (IntPtr) (-3);

		public const int minimizeRestoreDelay = 300;

		#region GetShellWindow

		[DllImport("user32.dll")]
		private static extern HWND GetShellWindow();

		public static readonly HWND shellWindow = GetShellWindow();

		#endregion

		#region EnumWindows

		public delegate bool EnumWindowsProc(HWND hWnd, LPARAM lParam);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EnumWindows([MarshalAs(UnmanagedType.FunctionPtr)] EnumWindowsProc lpfn, LPARAM lParam);

		#endregion

		#region GetWindowText/GetClassName

		public static string GetText(HWND hWnd)
		{
			var sb = new StringBuilder(256);
			GetWindowText(hWnd, sb, sb.Capacity);
			return sb.ToString();
		}

		public static string GetWindowClassName(HWND hWnd)
		{
			var sb = new StringBuilder(257);
			GetClassName(hWnd, sb, sb.Capacity);
			return sb.ToString();
		}

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern int GetWindowText(HWND hWnd, [Out] StringBuilder lpString, int nMaxCount);

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		private static extern int GetClassName(HWND hWnd, [Out] StringBuilder lpClassName, int nMaxCount);

		#endregion

		#region GetClassLongPtr

		public delegate IntPtr GetClassLongPtrDelegate(HWND hWnd, int nIndex);
		public static readonly GetClassLongPtrDelegate GetClassLongPtr;

		[DllImport("user32.dll", EntryPoint = "GetClassLong")]
		private static extern IntPtr GetClassLong32(HWND hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
		private static extern IntPtr GetClassLongPtr64(HWND hWnd, int nIndex);

		public const int GCL_HICONSM = -34;

		#endregion

		#region IsIconic/IsZoomed

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsIconic(HWND hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsZoomed(HWND hWnd);

		#endregion

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsWindowVisible(HWND hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsWindow([Optional] HWND hWnd);

		#region GetMenu/SetMenu/DestroyMenu

		[DllImport("user32.dll")]
		public static extern HMENU GetMenu(HWND hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetMenu(HWND hWnd, [Optional] HMENU hMenu);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool DestroyMenu(HWND hMenu);

		#endregion

		#region GetWindow

		[DllImport("user32.dll")]
		public static extern HWND GetWindow(HWND hWnd, GW uCmd);

		public enum GW : uint
		{
			GW_OWNER = 4,
		}

		#endregion

		#region FindWindow/FindWindowEx

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern HWND FindWindow([Optional, MarshalAs(UnmanagedType.LPTStr)] string className, [Optional, MarshalAs(UnmanagedType.LPTStr)] string windowText);

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		public static extern HWND FindWindowEx([Optional] HWND hwndParent, [Optional] HWND hwndChildAfter, [Optional, MarshalAs(UnmanagedType.LPTStr)] string lpszClass, [Optional, MarshalAs(UnmanagedType.LPTStr)] string lpszWindow);

		#endregion

		#region GetWindowLongPtr/SetWindowLongPtr

		public delegate UIntPtr SetWindowStyleLongPtrDelegate(HWND hWnd, WS dwNewLong);
		public static readonly SetWindowStyleLongPtrDelegate SetWindowStyleLongPtr;

		public delegate UIntPtr SetWindowExStyleLongPtrDelegate(HWND hWnd, WS_EX dwNewLong);
		public static readonly SetWindowExStyleLongPtrDelegate SetWindowExStyleLongPtr;

		public delegate WS GetWindowStyleLongPtrDelegate(HWND hWnd);
		public static readonly GetWindowStyleLongPtrDelegate GetWindowStyleLongPtr;

		public delegate WS_EX GetWindowExStyleLongPtrDelegate(HWND hWnd);
		public static readonly GetWindowExStyleLongPtrDelegate GetWindowExStyleLongPtr;

		[DllImport("user32.dll", EntryPoint = "SetWindowLong")]
		private static extern UIntPtr SetWindowLong32(HWND hWnd, int nIndex, WS dwNewLong);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
		private static extern UIntPtr SetWindowLongPtr64(HWND hWnd, int nIndex, WS dwNewLong);

		[DllImport("user32.dll", EntryPoint = "SetWindowLong")]
		private static extern UIntPtr SetWindowLong32(HWND hWnd, int nIndex, WS_EX dwNewLong);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
		private static extern UIntPtr SetWindowLongPtr64(HWND hWnd, int nIndex, WS_EX dwNewLong);

		[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
		private static extern WS GetWindowLong32WS(HWND hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
		private static extern WS GetWindowLongPtr64WS(HWND hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
		private static extern WS_EX GetWindowLong32WS_EX(HWND hWnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
		private static extern WS_EX GetWindowLongPtr64WS_EX(HWND hWnd, int nIndex);

		private const int GWL_STYLE = -16;
		private const int GWL_EXSTYLE = -20;

		[Flags]
		public enum WS : uint
		{
			WS_OVERLAPPED = 0,
			WS_POPUP = 0x80000000,
			WS_CHILD = 0x40000000,
			WS_MINIMIZE = 0x20000000,
			WS_VISIBLE = 0x10000000,
			WS_DISABLED = 0x8000000,
			WS_CLIPSIBLINGS = 0x4000000,
			WS_CLIPCHILDREN = 0x2000000,
			WS_MAXIMIZE = 0x1000000,
			WS_CAPTION = WS_BORDER | WS_DLGFRAME,
			WS_BORDER = 0x800000,
			WS_DLGFRAME = 0x400000,
			WS_VSCROLL = 0x200000,
			WS_HSCROLL = 0x100000,
			WS_SYSMENU = 0x80000,
			WS_THICKFRAME = 0x40000,
			WS_MINIMIZEBOX = 0x20000,
			WS_MAXIMIZEBOX = 0x10000,
			WS_TILED = WS_OVERLAPPED,
			WS_ICONIC = WS_MINIMIZE,
			WS_SIZEBOX = WS_THICKFRAME,
			WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX
		}

		[Flags]
		public enum WS_EX : uint
		{
			WS_EX_DLGMODALFRAME = 0x0001,
			WS_EX_NOPARENTNOTIFY = 0x0004,
			WS_EX_TOPMOST = 0x0008,
			WS_EX_ACCEPTFILES = 0x0010,
			WS_EX_TRANSPARENT = 0x0020,
			WS_EX_MDICHILD = 0x0040,
			WS_EX_TOOLWINDOW = 0x0080,
			WS_EX_WINDOWEDGE = 0x0100,
			WS_EX_CLIENTEDGE = 0x0200,
			WS_EX_CONTEXTHELP = 0x0400,
			WS_EX_RIGHT = 0x1000,
			WS_EX_LEFT = 0x0000,
			WS_EX_RTLREADING = 0x2000,
			WS_EX_LTRREADING = 0x0000,
			WS_EX_LEFTSCROLLBAR = 0x4000,
			WS_EX_RIGHTSCROLLBAR = 0x0000,
			WS_EX_CONTROLPARENT = 0x10000,
			WS_EX_STATICEDGE = 0x20000,
			WS_EX_APPWINDOW = 0x40000,
			WS_EX_OVERLAPPEDWINDOW = (WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE),
			WS_EX_PALETTEWINDOW = (WS_EX_WINDOWEDGE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST),
			WS_EX_LAYERED = 0x00080000,
			WS_EX_NOINHERITLAYOUT = 0x00100000, // Disable inheritence of mirroring by children
			WS_EX_LAYOUTRTL = 0x00400000, // Right to left mirroring
			WS_EX_COMPOSITED = 0x02000000,
			WS_EX_NOACTIVATE = 0x08000000
		}

		#endregion

		#region ShowWindow/ShowWindowAsync

		[DllImport("User32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ShowWindow(HWND hWnd, SW nCmdShow);

		[DllImport("User32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ShowWindowAsync(HWND hWnd, SW nCmdShow);

		public enum SW
		{
			SW_FORCEMINIMIZE = 11,
			SW_SHOW = 5,
			SW_SHOWNA = 8,
			SW_SHOWNOACTIVATE = 4,
			SW_SHOWMINNOACTIVE = 7,
			SW_SHOWMAXIMIZED = 3,
			SW_HIDE = 0,
			SW_RESTORE = 9,
			SW_MINIMIZE = 6,
			SW_SHOWMINIMIZED = 2,
			SW_SHOWNORMAL = 1
		}

		#endregion

		#region GetWindowPlacement/SetWindowPlacement

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetWindowPlacement(HWND hWnd, [In, Out] ref WINDOWPLACEMENT lpwndpl);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetWindowPlacement(HWND hWnd, [In] ref WINDOWPLACEMENT lpwndpl);

		private static readonly int WINDOWPLACEMENTSize = Marshal.SizeOf(typeof(WINDOWPLACEMENT));

		[StructLayout(LayoutKind.Sequential)]
		public struct POINT
		{
			public int X;
			public int Y;
		}

		[Flags]
		public enum WPF : uint
		{
			WPF_ASYNCWINDOWPLACEMENT = 0x0004,
			WPF_RESTORETOMAXIMIZED = 0x0002,
			WPF_SETMINPOSITION = 0x0001
		}

		/// <summary>
		/// Contains information about the placement of a window on the screen.
		/// </summary>
		[StructLayout(LayoutKind.Sequential)]
		public struct WINDOWPLACEMENT
		{
			/// <summary>
			/// The length of the structure, in bytes. Before calling the GetWindowPlacement or SetWindowPlacement functions, set this member to sizeof(WINDOWPLACEMENT).
			/// <para>
			/// GetWindowPlacement and SetWindowPlacement fail if this member is not set correctly.
			/// </para>
			/// </summary>
			public int Length;

			/// <summary>
			/// Specifies flags that control the position of the minimized window and the method by which the window is restored.
			/// </summary>
			public WPF Flags;

			/// <summary>
			/// The current show state of the window.
			/// </summary>
			public SW ShowCmd;

			/// <summary>
			/// The coordinates of the window's upper-left corner when the window is minimized.
			/// </summary>
			public POINT MinPosition;

			/// <summary>
			/// The coordinates of the window's upper-left corner when the window is maximized.
			/// </summary>
			public POINT MaxPosition;

			/// <summary>
			/// The window's coordinates when the window is in the restored position.
			/// </summary>
			public RECT NormalPosition;

			/// <summary>
			/// Gets the default (empty) value.
			/// </summary>
			public static WINDOWPLACEMENT Default
			{
				get
				{
					return new WINDOWPLACEMENT { Length = WINDOWPLACEMENTSize };
				}
			}
		}

		#endregion

		#region AdjustWindowRectEx/GetWindowRect/SetWindowPos/BeginDeferWindowPos/DeferWindowPos/EndDeferWindowPos

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool AdjustWindowRectEx([In, Out] ref RECT lpRect, WS dwStyle, [MarshalAs(UnmanagedType.Bool)] bool bMenu, WS_EX dwExStyle);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetWindowRect(HWND hwnd, [Out] out RECT lpRect);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetWindowPos(HWND hwnd, [Optional] HWND hwndInsertAfter,
			int x, int y, int width, int height, SWP flags);

		[Flags]
		public enum SWP : uint
		{
			SWP_SHOWWINDOW = 0x0040,
			SWP_HIDEWINDOW = 0x0080,
			SWP_NOZORDER = 0x0004,
			SWP_NOREDRAW = 0x0008,
			SWP_NOACTIVATE = 0x0010,
			SWP_NOMOVE = 0x0002,
			SWP_NOSIZE = 0x0001,
			SWP_FRAMECHANGED = 0x0020,
			SWP_NOCOPYBITS = 0x0100,
			SWP_NOOWNERZORDER = 0x0200,
			SWP_DEFERERASE = 0x2000,
			SWP_NOSENDCHANGING = 0x0400,
			SWP_ASYNCWINDOWPOS = 0x4000
		}

		public static readonly HWND HWND_BOTTOM = (HWND) 1;
		public static readonly HWND HWND_NOTOPMOST = (HWND) (-2);
		public static readonly HWND HWND_TOP = IntPtr.Zero;
		public static readonly HWND HWND_TOPMOST = (HWND) (-1);

		[DllImport("user32.dll")]
		public static extern HDWP BeginDeferWindowPos(int nNumWindows);

		[DllImport("user32.dll")]
		public static extern IntPtr DeferWindowPos(HDWP hWinPosInfo, HWND hWnd,
			 [Optional] HWND hWndInsertAfter, int x, int y, int cx, int cy, SWP uFlags);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EndDeferWindowPos(HDWP hWinPosInfo);

		#endregion

		#region GetForegroundWindow/SetForegroundWindow

		[DllImport("user32.dll")]
		public static extern HWND GetForegroundWindow();

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetForegroundWindow(HWND hWnd);

		#endregion

		#region RedrawWindow

		[Flags()]
		public enum RDW : uint
		{
			/// <summary>
			/// Invalidates the rectangle or region that you specify in lprcUpdate or hrgnUpdate.
			/// You can set only one of these parameters to a non-NULL value. If both are NULL, RDW_INVALIDATE invalidates the entire window.
			/// </summary>
			RDW_INVALIDATE = 0x1,

			/// <summary>Causes the OS to post a WM_PAINT message to the window regardless of whether a portion of the window is invalid.</summary>
			RDW_INTERNALPAINT = 0x2,

			/// <summary>
			/// Causes the window to receive a WM_ERASEBKGND message when the window is repainted.
			/// Specify this value in combination with the RDW_INVALIDATE value; otherwise, RDW_ERASE has no effect.
			/// </summary>
			RDW_ERASE = 0x4,

			/// <summary>
			/// Validates the rectangle or region that you specify in lprcUpdate or hrgnUpdate.
			/// You can set only one of these parameters to a non-NULL value. If both are NULL, RDW_VALIDATE validates the entire window.
			/// This value does not affect internal WM_PAINT messages.
			/// </summary>
			RDW_VALIDATE = 0x8,

			RDW_NOINTERNALPAINT = 0x10,

			/// <summary>Suppresses any pending WM_ERASEBKGND messages.</summary>
			RDW_NOERASE = 0x20,

			/// <summary>Excludes child windows, if any, from the repainting operation.</summary>
			RDW_NOCHILDREN = 0x40,

			/// <summary>Includes child windows, if any, in the repainting operation.</summary>
			RDW_ALLCHILDREN = 0x80,

			/// <summary>Causes the affected windows, which you specify by setting the RDW_ALLCHILDREN and RDW_NOCHILDREN values, to receive WM_ERASEBKGND and WM_PAINT messages before the RedrawWindow returns, if necessary.</summary>
			RDW_UPDATENOW = 0x100,

			/// <summary>
			/// Causes the affected windows, which you specify by setting the RDW_ALLCHILDREN and RDW_NOCHILDREN values, to receive WM_ERASEBKGND messages before RedrawWindow returns, if necessary.
			/// The affected windows receive WM_PAINT messages at the ordinary time.
			/// </summary>
			RDW_ERASENOW = 0x200,

			RDW_FRAME = 0x400,

			RDW_NOFRAME = 0x800
		}

		[DllImport("user32.dll")]
		public static extern bool RedrawWindow(HWND hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, RDW flags);

		#endregion

		#region GetLastActivePopup/ShowOwnedPopups

		[DllImport("user32.dll")]
		public static extern HWND GetLastActivePopup(HWND hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ShowOwnedPopups(IntPtr HWND, [MarshalAs(UnmanagedType.Bool)] bool fShow);

		#endregion

		// icon stuff

		#region CopyIcon/DestroyIcon

		[DllImport("user32.dll")]
		public static extern HICON CopyIcon(HICON hIcon);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool DestroyIcon(HICON hIcon);

		#endregion

		#region SHGetFileInfo

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		public struct SHFILEINFO
		{
			public HICON hIcon;
			public int iIcon;
			public DWORD dwAttributes;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
			public string szDisplayName;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
			public string szTypeName;
		};

		public const uint SHGFI_ICON = 0x100;
		public const uint SHGFI_SMALLICON = 0x1;

		[DllImport("shell32.dll", CharSet = CharSet.Auto)]
		public static extern UIntPtr SHGetFileInfo([MarshalAs(UnmanagedType.LPTStr)] string pszPath, DWORD dwFileAttributes, [In, Out] ref SHFILEINFO psfi, int cbSizeFileInfo, UINT uFlags);

		#endregion

		// keyboard stuff

		#region SendInput

		[DllImport("user32.dll")]
		public static extern UINT SendInput(UINT nInputs, INPUT[] pInputs, int cbSize);

		[StructLayout(LayoutKind.Sequential)]
		public struct MouseInputData
		{
			public int dx;
			public int dy;
			public uint mouseData;
			public uint dwFlags;
			public uint time;
			public IntPtr dwExtraInfo;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct KEYBDINPUT
		{
			public ushort wVk;
			public ushort wScan;
			public uint dwFlags;
			public uint time;
			public IntPtr dwExtraInfo;
		}

		[StructLayout(LayoutKind.Explicit)]
		public struct MouseKeybdhardwareInputUnion
		{
			[FieldOffset(0)]
			public MouseInputData mi;
			[FieldOffset(0)]
			public KEYBDINPUT ki;
		}

		private static readonly UIntPtr INPUT_KEYBOARD = (UIntPtr) 1;

		[StructLayout(LayoutKind.Sequential)]
		public struct INPUT
		{
			public UIntPtr type;
			public MouseKeybdhardwareInputUnion mkhi;

			public INPUT(System.Windows.Forms.Keys key, uint flags)
			{
				type = INPUT_KEYBOARD;
				mkhi = new MouseKeybdhardwareInputUnion { ki = new KEYBDINPUT { wVk = (ushort) key, dwFlags = flags } };
			}
		}

		public static readonly int INPUTSize = Marshal.SizeOf(typeof(INPUT));

		public const uint KEYEVENTF_KEYUP = 0x0002;

		#endregion

		#region RegisterHotKey/UnregisterHotKey

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool RegisterHotKey([Optional] HWND hWnd, int id, MOD fsModifiers, System.Windows.Forms.Keys vk);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool UnregisterHotKey([Optional] HWND hWnd, int id);

		[Flags]
		public enum MOD : uint
		{
			MOD_ALT = 0x1,
			MOD_CONTROL = 0x2,
			MOD_SHIFT = 0x4,
			MOD_WIN = 0x8,
			MOD_NOREPEAT = 0x4000
		}

		public const int WM_HOTKEY = 0x312;

		#endregion

		#region GetKeyState/GetAsyncKeyState

		[DllImport("user32.dll")]
		public static extern short GetKeyState(System.Windows.Forms.Keys nVirtKey);

		[DllImport("user32.dll")]
		public static extern short GetAsyncKeyState(System.Windows.Forms.Keys nVirtKey);

		#endregion

		// process and thread stuff

		#region Is64BitProcess

		[DllImport("kernel32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsWow64Process(HANDLE processHandle, [Out, MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

		public static bool Is64BitProcess(HWND hWnd)
		{
			if (Environment.Is64BitOperatingSystem)
			{
				var result = false;

				int processId;
				GetWindowThreadProcessId(hWnd, out processId);
				var processHandle = Windawesome.isAtLeastVista ?
					OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId) :
					OpenProcess(PROCESS_QUERY_INFORMATION, false, processId);
				if (processHandle != IntPtr.Zero)
				{
					try
					{
						IsWow64Process(processHandle, out result);
						result = !result;
					}
					finally
					{
						CloseHandle(processHandle);
					}
				}

				return result;
			}
			else
			{
				return false;
			}
		}

		#endregion

		/*
		#region GetProcessOwner

		[StructLayout(LayoutKind.Sequential)]
		private struct TOKEN_USER
		{
			public SID_AND_ATTRIBUTES User;
		}

		[DllImport("kernel32.dll")]
		private static extern HLOCAL LocalFree(HLOCAL hMem);

		[DllImport("advapi32.dll", CharSet = CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ConvertSidToStringSid(HSID pSID, [Out] out IntPtr pStringSid);

		public static string GetProcessOwner(int processId)
		{
			var result = String.Empty;

			HANDLE tokenHandle;
			var processHandle = Windawesome.isAtLeastVista ?
				OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId) :
				OpenProcess(PROCESS_QUERY_INFORMATION, false, processId);
			if (processHandle != IntPtr.Zero)
			{
				if (OpenProcessToken(processHandle, TOKEN_QUERY, out tokenHandle))
				{
					int requiredLength;

					GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenUser, IntPtr.Zero, 0, out requiredLength);

					var tokenInformation = Marshal.AllocHGlobal(requiredLength);
					if (GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenUser, tokenInformation, requiredLength, out requiredLength))
					{
						LPVOID sidString;
						if (ConvertSidToStringSid(((TOKEN_USER) Marshal.PtrToStructure(tokenInformation, typeof(TOKEN_USER))).User.Sid, out sidString))
						{
							result = Marshal.PtrToStringAuto(sidString);
							LocalFree(sidString);
							result = new System.Security.Principal.SecurityIdentifier(result).Translate(typeof(System.Security.Principal.NTAccount)).ToString();
						}
					}
					CloseHandle(tokenHandle);
					Marshal.FreeHGlobal(tokenInformation);
				}
				CloseHandle(processHandle);
			}
			else
			{
				var searcher = new ManagementObjectSearcher();
				searcher.Query = new ObjectQuery("SELECT * FROM Win32_Process WHERE ProcessID = " + processId);
				searcher.Options.Timeout = TimeSpan.FromSeconds(3);

				foreach (ManagementObject obj in searcher.Get())
				{
					 var args = new string[] { string.Empty, string.Empty };
					 if (Convert.ToInt32(obj.InvokeMethod("GetOwner", args)) == 0)
					 {
						 return args[1] + "\\" + args[0];
					 }
				}
			}

			return result;
		}

		#endregion
		*/

		#region IsCurrentProcessElevatedInRespectToShell

		public const DWORD TOKEN_QUERY = 0x00000008;

		public const int SECURITY_NT_AUTHORITY = 5;
		public const uint SECURITY_BUILTIN_DOMAIN_RID = 0x20;
		public const uint DOMAIN_ALIAS_RID_ADMINS = 0x220;

		public static readonly int SID_AND_ATTRIBUTESSize = Marshal.SizeOf(typeof(SID_AND_ATTRIBUTES));

		public enum TOKEN_INFORMATION_CLASS
		{
			TokenUser = 1,
			TokenGroups = 2,
			TokenElevationType = 18,
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct SID_AND_ATTRIBUTES
		{
			public HSID Sid;
			public DWORD Attributes;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct SID_IDENTIFIER_AUTHORITY
		{
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 6, ArraySubType = UnmanagedType.I1)]
			public byte[] Value;
		}

		public struct TOKEN_ELEVATION
		{
			public DWORD TokenIsElevated;
		}

		public enum TOKEN_ELEVATION_TYPE : uint
		{
			TokenElevationTypeDefault = 1,
			TokenElevationTypeFull = 2,
			TokenElevationTypeLimited = 3
		} 

		[DllImport("advapi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool OpenProcessToken(HANDLE ProcessHandle, DWORD DesiredAccess, [Out] out HANDLE TokenHandle);

		[DllImport("advapi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetTokenInformation(HANDLE hToken, TOKEN_INFORMATION_CLASS tokenInfoClass, [Optional, Out] LPVOID TokenInformation, int tokeInfoLength, [Out] out int reqLength);

		[DllImport("advapi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool AllocateAndInitializeSid(
			[In] ref SID_IDENTIFIER_AUTHORITY pIdentifierAuthority,
			byte nSubAuthorityCount,
			DWORD dwSubAuthority0, DWORD dwSubAuthority1,
			DWORD dwSubAuthority2, DWORD dwSubAuthority3,
			DWORD dwSubAuthority4, DWORD dwSubAuthority5,
			DWORD dwSubAuthority6, DWORD dwSubAuthority7,
			[Out] out IntPtr pSid);

		[DllImport("advapi32.dll")]
		public static extern PVOID FreeSid(IntPtr pSid);

		[DllImport("advapi32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EqualSid(IntPtr pSid1, IntPtr pSid2);

		internal static bool IsCurrentProcessElevatedInRespectToShell()
		{
			var result = false;

			IntPtr authenticatedUsersSid;
			var ntAuthority = new SID_IDENTIFIER_AUTHORITY { Value = new byte[] { 0, 0, 0, 0, 0, SECURITY_NT_AUTHORITY } };
			AllocateAndInitializeSid(ref ntAuthority, 2, SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS,
				0, 0, 0, 0, 0, 0, out authenticatedUsersSid);

			HANDLE currentProcessTokenHandle;
			if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out currentProcessTokenHandle))
			{
				var windawesomeIsRunAsAdmin = TokenIsInAdministratorsGroup(currentProcessTokenHandle, authenticatedUsersSid);

				if (windawesomeIsRunAsAdmin)
				{
					LONG shellProcessId;
					GetWindowThreadProcessId(shellWindow, out shellProcessId);
					var shellProcessHandle = Windawesome.isAtLeastVista ?
						OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, shellProcessId) :
						OpenProcess(PROCESS_QUERY_INFORMATION, false, shellProcessId);

					if (shellProcessHandle != IntPtr.Zero)
					{
						HANDLE shellProcessTokenHandle;
						if (OpenProcessToken(shellProcessHandle, TOKEN_QUERY, out shellProcessTokenHandle))
						{
							result = Windawesome.isAtLeastVista
								? IsProcessElevated(currentProcessTokenHandle) && !IsProcessElevated(shellProcessTokenHandle)
								: !TokenIsInAdministratorsGroup(shellProcessTokenHandle, authenticatedUsersSid);

							CloseHandle(shellProcessTokenHandle);
						}

						CloseHandle(shellProcessHandle);
					}
				}
				else if (Windawesome.isAtLeastVista)
				{
					result = IsProcessElevated(GetCurrentProcess());
				}
	
				FreeSid(authenticatedUsersSid);

				CloseHandle(currentProcessTokenHandle);
			}

			return result;
		}

		private static bool TokenIsInAdministratorsGroup(HANDLE tokenHandle, IntPtr authenticatedUsersSid)
		{
			var result = false;

			int requiredLength;
			GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenGroups, IntPtr.Zero, 0, out requiredLength);

			var tokenInformation = Marshal.AllocHGlobal(requiredLength);
			if (GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenGroups, tokenInformation, requiredLength, out requiredLength))
			{
				var groupsCount = Marshal.ReadInt32(tokenInformation);
				var counter = tokenInformation + IntPtr.Size; // This is IntPtr.Size although groupsCount is a DWORD because of padding
				for (var i = 0; i < groupsCount; i++)
				{
					var sid = Marshal.ReadIntPtr(counter);

					if (EqualSid(authenticatedUsersSid, sid))
					{
						result = true;
						break;
					}

					counter += SID_AND_ATTRIBUTESSize;
				}
			}

			Marshal.FreeHGlobal(tokenInformation);

			return result;
		}

		private static bool IsProcessElevated(HANDLE tokenHandle)
		{
			var result = false;

			var tokenElevationType = Marshal.AllocHGlobal(4); // Marshal.SizeOf(typeof(TOKEN_ELEVATION_TYPE))
			int requiredLength;
			if (GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenElevationType, tokenElevationType, 4, out requiredLength))
			{
				result = (TOKEN_ELEVATION_TYPE) Marshal.ReadInt32(tokenElevationType) == TOKEN_ELEVATION_TYPE.TokenElevationTypeFull;
			}

			Marshal.FreeHGlobal(tokenElevationType);

			return result;
		}

		#endregion

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool AttachThreadInput(DWORD idAttach, DWORD idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

		[DllImport("user32.dll")]
		public static extern DWORD GetWindowThreadProcessId(HWND hWnd, [Optional, Out] out int lpdwProcessId);

		[DllImport("user32.dll")]
		public static extern DWORD GetWindowThreadProcessId(HWND hWnd, [Optional, Out] IntPtr lpdwProcessId);

		#region GetCurrentThreadId/GetCurrentProcess

		[DllImport("kernel32.dll")]
		public static extern DWORD GetCurrentThreadId();

		[DllImport("kernel32.dll")]
		public static extern HANDLE GetCurrentProcess();

		#endregion

		#region OpenProcess/CloseHandle

		public const DWORD PROCESS_VM_READ = 0x10;
		public const DWORD PROCESS_VM_OPERATION = 0x8;
		public const DWORD PROCESS_QUERY_INFORMATION = 0x400;
		public const DWORD PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

		[DllImport("kernel32.dll")]
		public static extern HANDLE OpenProcess(DWORD dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

		[DllImport("kernel32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool CloseHandle(HANDLE hObject);

		#endregion

		// misc stuff

		#region struct RECT

		[StructLayout(LayoutKind.Sequential)]
		public struct RECT
		{
			public int left;
			public int top;
			public int right;
			public int bottom;

			public static RECT FromRectangle(Rectangle r)
			{
				return new RECT { bottom = r.Bottom, left = r.Left, right = r.Right, top = r.Top };
			}

			public Rectangle ToRectangle()
			{
				return Rectangle.FromLTRB(left, top, right, bottom);
			}
		}

		#endregion

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetCursorPos(int X, int Y);

		#region GlobalAddAtom/GlobalDeleteAtom

		[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
		public static extern ushort GlobalAddAtom([MarshalAs(UnmanagedType.LPTStr)] string atomName);

		[DllImport("kernel32.dll")]
		public static extern ushort GlobalDeleteAtom(ushort nAtom);

		#endregion

		#region Solution Native DLL functions

		public delegate void RunApplicationNonElevatedDelegate(string path, string arguments);
		public static readonly RunApplicationNonElevatedDelegate RunApplicationNonElevated;

		public delegate bool UnsubclassWindowDelegate(IntPtr hWnd);
		public static readonly UnsubclassWindowDelegate UnsubclassWindow;

		public delegate bool RegisterSystemTrayHookDelegate(IntPtr hwnd);
		public static readonly RegisterSystemTrayHookDelegate RegisterSystemTrayHook;

		public delegate bool UnregisterSystemTrayHookDelegate();
		public static readonly UnregisterSystemTrayHookDelegate UnregisterSystemTrayHook;

		[DllImport("Helpers32.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, EntryPoint = "RunApplicationNonElevated")]
		private static extern void RunApplicationNonElevated32([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPWStr)] string arguments);

		[DllImport("Helpers64.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, EntryPoint = "RunApplicationNonElevated")]
		private static extern void RunApplicationNonElevated64([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPWStr)] string arguments);

		[DllImport("WindowSubclassing32.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SubclassWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SubclassWindow32(IntPtr hwnd, IntPtr window);

		[DllImport("WindowSubclassing64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SubclassWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SubclassWindow64(IntPtr hwnd, IntPtr window);

		[DllImport("WindowSubclassing32.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "UnsubclassWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool UnsubclassWindow32(IntPtr hwnd);

		[DllImport("WindowSubclassing64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "UnsubclassWindow")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool UnsubclassWindow64(IntPtr hwnd);

		[DllImport("SystemTrayHook32.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "RegisterSystemTrayHook")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool RegisterSystemTrayHook32(IntPtr hwnd);

		[DllImport("SystemTrayHook64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "RegisterSystemTrayHook")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool RegisterSystemTrayHook64(IntPtr hwnd);

		[DllImport("SystemTrayHook32.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "UnregisterSystemTrayHook")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool UnregisterSystemTrayHook32();

		[DllImport("SystemTrayHook64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "UnregisterSystemTrayHook")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool UnregisterSystemTrayHook64();

		#endregion

		public static readonly IntPtr IntPtrOne = (IntPtr) 1;

		#region System Tray Structures

		/// <summary>
		/// The state of the icon - can be set to
		/// hide the icon.
		/// </summary>
		[Flags]
		public enum NIS : uint
		{
			/// <summary>
			/// The icon is visible.
			/// </summary>
			Visible = 0x00,
			/// <summary>
			/// Hide the icon.
			/// </summary>
			NIS_HIDDEN = 0x01,

			/// <summary>
			/// The icon is shared - currently not supported, thus commented out.
			/// </summary>
			NIS_SHAREDICON = 0x02
		}

		/// <summary>
		/// Indicates which members of a <see cref="NOTIFYICONDATA"/> structure
		/// were set, and thus contain valid data or provide additional information
		/// to the ToolTip as to how it should display.
		/// </summary>
		[Flags]
		public enum NIF : uint
		{
			/// <summary>
			/// The message ID is set.
			/// </summary>
			NIF_MESSAGE = 0x01,
			/// <summary>
			/// The notification icon is set.
			/// </summary>
			NIF_ICON = 0x02,
			/// <summary>
			/// The tooltip is set.
			/// </summary>
			NIF_TIP = 0x04,
			/// <summary>
			/// State information (<see cref="NIS"/>) is set. This
			/// applies to both <see cref="NOTIFYICONDATA.dwState"/> and
			/// <see cref="NOTIFYICONDATA.dwStateMask"/>.
			/// </summary>
			NIF_STATE = 0x08,
			/// <summary>
			/// The ballon ToolTip is set. Accordingly, the following
			/// members are set: <see cref="NOTIFYICONDATA.BalloonText"/>,
			/// <see cref="NOTIFYICONDATA.szInfoTitle"/>, <see cref="NOTIFYICONDATA.dwInfoFlags"/>,
			/// and <see cref="NOTIFYICONDATA.uTimeout"/>.
			/// </summary>
			NIF_INFO = 0x10,

			/// <summary>
			/// public identifier is set. Reserved, thus commented out.
			/// </summary>
			NIF_GUID = 0x20,

			/// <summary>
			/// Windows Vista (Shell32.dll version 6.0.6) and later. If the ToolTip
			/// cannot be displayed immediately, discard it.<br/>
			/// Use this flag for ToolTips that represent real-time information which
			/// would be meaningless or misleading if displayed at a later time.
			/// For example, a message that states "Your telephone is ringing."<br/>
			/// This modifies and must be combined with the <see cref="NIF_INFO"/> flag.
			/// </summary>
			NIF_REALTIME = 0x40,
			/// <summary>
			/// Windows Vista (Shell32.dll version 6.0.6) and later.
			/// Use the standard ToolTip. Normally, when uVersion is set
			/// to NOTIFYICON_VERSION_4, the standard ToolTip is replaced
			/// by the application-drawn pop-up user interface (UI).
			/// If the application wants to show the standard tooltip
			/// in that case, regardless of whether the on-hover UI is showing,
			/// it can specify NIF_SHOWTIP to indicate the standard tooltip
			/// should still be shown.<br/>
			/// Note that the NIF_SHOWTIP flag is effective until the next call
			/// to Shell_NotifyIcon.
			/// </summary>
			NIF_SHOWTIP = 0x80
		}

		// this is a 64-bit structure, when running in 64-bit mode, but should be 32!!!
		/// <summary>
		/// A struct that is submitted in order to configure
		/// the taskbar icon. Provides various members that
		/// can be configured partially, according to the
		/// values of the <see cref="NIF"/>
		/// that were defined.
		/// </summary>
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		public struct NOTIFYICONDATA
		{
			/// <summary>
			/// Size of this structure, in bytes.
			/// </summary>
			public uint cbSize;

			/// <summary>
			/// Handle to the window that receives notification messages associated with an icon in the
			/// taskbar status area. The Shell uses hWnd and uID to identify which icon to operate on
			/// when Shell_NotifyIcon is invoked.
			/// </summary>
			//public IntPtr hWnd;
			public int hWnd;

			/// <summary>
			/// Application-defined identifier of the taskbar icon. The Shell uses hWnd and uID to identify
			/// which icon to operate on when Shell_NotifyIcon is invoked. You can have multiple icons
			/// associated with a single hWnd by assigning each a different uID. This feature, however
			/// is currently not used.
			/// </summary>
			public uint uID;

			/// <summary>
			/// Flags that indicate which of the other members contain valid data. This member can be
			/// a combination of the NIF_XXX constants.
			/// </summary>
			public NIF uFlags;

			/// <summary>
			/// Application-defined message identifier. The system uses this identifier to send
			/// notifications to the window identified in hWnd.
			/// </summary>
			public uint uCallbackMessage;

			/// <summary>
			/// A handle to the icon that should be displayed. Just
			/// <see cref="Icon.Handle"/>.
			/// </summary>
			//public IntPtr hIcon;
			public int hIcon;

			/// <summary>
			/// String with the text for a standard ToolTip. It can have a maximum of 64 characters including
			/// the terminating NULL. For Version 5.0 and later, szTip can have a maximum of
			/// 128 characters, including the terminating NULL.
			/// </summary>
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string szTip;

			/// <summary>
			/// State of the icon. Remember to also set the <see cref="dwStateMask"/>.
			/// </summary>
			public NIS dwState;

			/// <summary>
			/// A value that specifies which bits of the state member are retrieved or modified.
			/// For example, setting this member to <see cref="NIS.NIS_HIDDEN"/>
			/// causes only the item's hidden
			/// state to be retrieved.
			/// </summary>
			public NIS dwStateMask;

			/// <summary>
			/// String with the text for a balloon ToolTip. It can have a maximum of 255 characters.
			/// To remove the ToolTip, set the NIF_INFO flag in uFlags and set szInfo to an empty string.
			/// </summary>
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
			public string szInfo;

			/// <summary>
			/// Mainly used to set the version when <see cref="WinApi.Shell_NotifyIcon"/> is invoked
			/// with <see cref="NotifyCommand.SetVersion"/>. However, for legacy operations,
			/// the same member is also used to set timouts for balloon ToolTips.
			/// </summary>
			public uint uTimeout;

			/// <summary>
			/// String containing a title for a balloon ToolTip. This title appears in boldface
			/// above the text. It can have a maximum of 63 characters.
			/// </summary>
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
			public string szInfoTitle;

			/// <summary>
			/// Adds an icon to a balloon ToolTip, which is placed to the left of the title. If the
			/// <see cref="szInfoTitle"/> member is zero-length, the icon is not shown.
			/// </summary>
			public uint dwInfoFlags;

			/// <summary>
			/// Windows XP (Shell32.dll version 6.0) and later.<br/>
			/// - Windows 7 and later: A registered GUID that identifies the icon.
			/// 	This value overrides uID and is the recommended method of identifying the icon.<br/>
			/// - Windows XP through Windows Vista: Reserved.
			/// </summary>
			public Guid guidItem;

			/// <summary>
			/// Windows Vista (Shell32.dll version 6.0.6) and later. The handle of a customized
			/// balloon icon provided by the application that should be used independently
			/// of the tray icon. If this member is non-NULL and the <see cref="Interop.BalloonFlags.User"/>
			/// flag is set, this icon is used as the balloon icon.<br/>
			/// If this member is NULL, the legacy behavior is carried out.
			/// </summary>
			//public IntPtr hBalloonIcon;
			public uint hBalloonIcon;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct COPYDATASTRUCT
		{
			public IntPtr dwData;
			public int cbData;
			public IntPtr lpData;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct SHELLTRAYDATA
		{
			public int dwHz;
			public NIM dwMessage;
			public NOTIFYICONDATA nid;
		}

		public enum NIM
		{
			NIM_ADD = 0,
			NIM_MODIFY = 1,
			NIM_DELETE = 2
		}

		public static readonly IntPtr SH_TRAY_DATA = NativeMethods.IntPtrOne;
		public const int WM_COPYDATA = 0x004A;

		public enum TBSTATE : byte
		{
			TBSTATE_HIDDEN = 0x08
		}

		#region TBBUTTON/TRAYDATA

		public interface ITBBUTTON
		{
			int iBitmap { get; set; }
			int idCommand { get; set; }
			TBSTATE fsState { get; set; }
			byte fsStyle { get; set; }
			IntPtr dwData { get; set; }
			IntPtr iString { get; set; }
			bool Initialize(IntPtr explorerProcessHandle, IntPtr buttonMemory);
		}

		// TBBUTTON for 32-bit Windows
		public class TBBUTTON32 : ITBBUTTON
		{
			[StructLayout(LayoutKind.Explicit, Size = 20)]
			public struct ButtonData
			{
				[FieldOffset(0)]
				public int iBitmap;
				[FieldOffset(4)]
				public int idCommand;
				[FieldOffset(8)]
				public TBSTATE fsState;
				[FieldOffset(9)]
				public byte fsStyle;
				[FieldOffset(12)]
				public IntPtr dwData;
				[FieldOffset(16)]
				public IntPtr iString;
			}

			private ButtonData data;

			public int iBitmap
			{
				get { return data.iBitmap; }
				set { data.iBitmap = value; }
			}

			public int idCommand
			{
				get { return data.idCommand; }
				set { data.idCommand = value; }
			}

			public TBSTATE fsState
			{
				get { return data.fsState; }
				set { data.fsState = value; }
			}

			public byte fsStyle
			{
				get { return data.fsStyle; }
				set { data.fsStyle = value; }
			}

			public IntPtr dwData
			{
				get { return data.dwData; }
				set { data.dwData = value; }
			}

			public IntPtr iString
			{
				get { return data.iString; }
				set { data.iString = value; }
			}

			unsafe public bool Initialize(IntPtr explorerProcessHandle, IntPtr buttonMemory)
			{
				fixed (void* ptr = &this.data)
				{
					return ReadProcessMemory(explorerProcessHandle, buttonMemory, ptr, (IntPtr) sizeof(ButtonData), UIntPtr.Zero);
				}
			}
		}

		// TBBUTTON for 64-bit Windows
		public class TBBUTTON64 : ITBBUTTON
		{
			[StructLayout(LayoutKind.Explicit, Size = 32)]
			public struct ButtonData
			{
				[FieldOffset(0)]
				public int iBitmap;
				[FieldOffset(4)]
				public int idCommand;
				[FieldOffset(8)]
				public TBSTATE fsState;
				[FieldOffset(9)]
				public byte fsStyle;
				[FieldOffset(16)]
				public IntPtr dwData;
				[FieldOffset(24)]
				public IntPtr iString;
			}

			private ButtonData data;

			public int iBitmap
			{
				get { return data.iBitmap; }
				set { data.iBitmap = value; }
			}

			public int idCommand
			{
				get { return data.idCommand; }
				set { data.idCommand = value; }
			}

			public TBSTATE fsState
			{
				get { return data.fsState; }
				set { data.fsState = value; }
			}

			public byte fsStyle
			{
				get { return data.fsStyle; }
				set { data.fsStyle = value; }
			}

			public IntPtr dwData
			{
				get { return data.dwData; }
				set { data.dwData = value; }
			}

			public IntPtr iString
			{
				get { return data.iString; }
				set { data.iString = value; }
			}

			unsafe public bool Initialize(IntPtr explorerProcessHandle, IntPtr buttonMemory)
			{
				fixed (void* ptr = &this.data)
				{
					return ReadProcessMemory(explorerProcessHandle, buttonMemory, ptr, (IntPtr) sizeof(ButtonData), UIntPtr.Zero);
				}
			}
		}

		public interface ITRAYDATA
		{
			IntPtr hWnd { get; set; }
			uint uID { get; set; }
			uint uCallbackMessage { get; set; }
			NIF uFlags { get; set; }
			uint dwUnknown { get; set; }
			IntPtr hIcon { get; set; }
			IntPtr lpszTip { get; set; } // String[64]
			NIS dwState { get; set; }
			NIS dwStateMask { get; set; }
			bool Initialize(IntPtr explorerProcessHandle, IntPtr dwData);
		}

		// TRAYDATA for 32-bit Windows
		public class TRAYDATA32 : ITRAYDATA
		{
			[StructLayout(LayoutKind.Explicit, Size = 36)]
			private struct TrayData
			{
				[FieldOffset(0)]
				public IntPtr hWnd;
				[FieldOffset(4)]
				public uint uID;
				[FieldOffset(8)]
				public uint uCallbackMessage;
				[FieldOffset(12)]
				public NIF uFlags;
				[FieldOffset(16)]
				public uint dwUnknown;
				[FieldOffset(20)]
				public IntPtr hIcon;
				[FieldOffset(24)]
				public IntPtr lpszTip; // String[64]
				[FieldOffset(28)]
				public NIS dwState;
				[FieldOffset(32)]
				public NIS dwStateMask;
				//[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
				//public string lpszInfo;
				//public uint dwUnion;
				//public IntPtr lpszInfoTitle; //String[64]
				//public IntPtr lpszInfoTitle2; //String[64]
				//public uint dwInfoFlags; // Used when creating a notifyicon
			}

			private TrayData data;

			public IntPtr hWnd
			{
				get { return data.hWnd; }
				set { data.hWnd = value; }
			}

			public uint uID
			{
				get { return data.uID; }
				set { data.uID = value; }
			}

			public uint uCallbackMessage
			{
				get { return data.uCallbackMessage; }
				set { data.uCallbackMessage = value; }
			}

			public NIF uFlags
			{
				get { return data.uFlags; }
				set { data.uFlags = value; }
			}

			public uint dwUnknown
			{
				get { return data.dwUnknown; }
				set { data.dwUnknown = value; }
			}

			public IntPtr hIcon
			{
				get { return data.hIcon; }
				set { data.hIcon = value; }
			}

			public IntPtr lpszTip
			{
				get { return data.lpszTip; }
				set { data.lpszTip = value; }
			}

			public NIS dwState
			{
				get { return data.dwState; }
				set { data.dwState = value; }
			}

			public NIS dwStateMask
			{
				get { return data.dwStateMask; }
				set { data.dwStateMask = value; }
			}

			unsafe public bool Initialize(IntPtr explorerProcessHandle, IntPtr dwData)
			{
				fixed (void* ptr = &this.data)
				{
					return ReadProcessMemory(explorerProcessHandle, dwData, ptr, (IntPtr) sizeof(TrayData), UIntPtr.Zero);
				}
			}
		}

		// TRAYDATA for 64-bit Windows
		public class TRAYDATA64 : ITRAYDATA
		{
			[StructLayout(LayoutKind.Explicit, Size = 52)]
			private struct TrayData
			{
				[FieldOffset(0)]
				public IntPtr hWnd;
				[FieldOffset(8)]
				public uint uID;
				[FieldOffset(12)]
				public uint uCallbackMessage;
				[FieldOffset(16)]
				public NIF uFlags;
				[FieldOffset(20)]
				public uint dwUnknown;
				[FieldOffset(24)]
				public IntPtr hIcon;
				[FieldOffset(32)]
				public IntPtr lpszTip; // String[64]
				[FieldOffset(40)]
				public NIS dwState;
				[FieldOffset(48)]
				public NIS dwStateMask;
				//[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
				//public char lpszInfo;
				//public uint dwUnion;
				//public IntPtr lpszInfoTitle; //String[64]
				//public uint dwInfoFlags; // Used when creating a notifyicon
			}

			private TrayData data;

			public IntPtr hWnd
			{
				get { return data.hWnd; }
				set { data.hWnd = value; }
			}

			public uint uID
			{
				get { return data.uID; }
				set { data.uID = value; }
			}

			public uint uCallbackMessage
			{
				get { return data.uCallbackMessage; }
				set { data.uCallbackMessage = value; }
			}

			public NIF uFlags
			{
				get { return data.uFlags; }
				set { data.uFlags = value; }
			}

			public uint dwUnknown
			{
				get { return data.dwUnknown; }
				set { data.dwUnknown = value; }
			}

			public IntPtr hIcon
			{
				get { return data.hIcon; }
				set { data.hIcon = value; }
			}

			public IntPtr lpszTip
			{
				get { return data.lpszTip; }
				set { data.lpszTip = value; }
			}

			public NIS dwState
			{
				get { return data.dwState; }
				set { data.dwState = value; }
			}

			public NIS dwStateMask
			{
				get { return data.dwStateMask; }
				set { data.dwStateMask = value; }
			}

			unsafe public bool Initialize(IntPtr explorerProcessHandle, IntPtr dwData)
			{
				fixed (void* ptr = &this.data)
				{
					return ReadProcessMemory(explorerProcessHandle, dwData, ptr, (IntPtr) sizeof(TrayData), UIntPtr.Zero);
				}
			}
		}

		#endregion

		#endregion

		#region VirtualAllocEx/VirtualFreeEx/ReadProcessMemory

		public const uint MEM_COMMIT = 0x1000;
		public const uint MEM_RELEASE = 0x8000;
		public const uint PAGE_READWRITE = 0x4;

		[DllImport("kernel32.dll")]
		public static extern LPVOID VirtualAllocEx(HANDLE hProcess, [Optional] LPVOID lpAddress, SIZE_T dwSize, DWORD flAllocationType, DWORD flProtect);

		[DllImport("kernel32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool VirtualFreeEx(HANDLE hProcess, LPVOID lpAddress, SIZE_T dwSize, DWORD dwFreeType);

		[DllImport("kernel32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		unsafe public static extern bool ReadProcessMemory(HANDLE hProcess, IntPtr lpBaseAddress, [Out] void* lpBuffer, SIZE_T nSize, [Out] UIntPtr lpNumberOfBytesRead);

		[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ReadProcessMemory(HANDLE hProcess, IntPtr lpBaseAddress, [Out] StringBuilder lpBuffer, SIZE_T nSize, [Out] UIntPtr lpNumberOfBytesRead);

		#endregion

		#region SystemParametersInfo

		[Flags]
		public enum SPIF : uint
		{
			SPIF_UPDATEINIFILE = 0x01,
			SPIF_SENDCHANGE = 0x02
		}

		public enum SPI : uint
		{
			SPI_GETANIMATION = 0x0048,
			SPI_SETANIMATION = 0x0049,
			SPI_GETNONCLIENTMETRICS = 0x0029,
			SPI_SETNONCLIENTMETRICS = 0x002A,
			SPI_GETMOUSEVANISH = 0x1020,
			SPI_SETMOUSEVANISH = 0x1021,
			SPI_GETACTIVEWINDOWTRACKING = 0x1000,
			SPI_SETACTIVEWINDOWTRACKING = 0x1001,
			SPI_GETACTIVEWNDTRKZORDER = 0x100C,
			SPI_SETACTIVEWNDTRKZORDER = 0x100D,
		}

		[DllImport("user32.dll", CharSet = CharSet.Auto)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SystemParametersInfo(SPI uiAction, int uiParam, [In, Out] ref NONCLIENTMETRICS pvParam, SPIF fWinIni);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SystemParametersInfo(SPI uiAction, int uiParam, [In, Out] ref ANIMATIONINFO pvParam, SPIF fWinIni);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SystemParametersInfo(SPI uiAction, int uiParam, [In, Out, MarshalAs(UnmanagedType.Bool)] ref bool pvParam, SPIF fWinIni);

		private static readonly int ANIMATIONINFOSize = Marshal.SizeOf(typeof (ANIMATIONINFO));

		[StructLayout(LayoutKind.Sequential)]
		public struct ANIMATIONINFO
		{
			public int cbSize;
			public int iMinAnimate;

			public static ANIMATIONINFO Default { get { return new ANIMATIONINFO { cbSize = ANIMATIONINFOSize }; } }
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		public struct LOGFONT
		{
			public int lfHeight;
			public int lfWidth;
			public int lfEscapement;
			public int lfOrientation;
			public int lfWeight;
			public byte lfItalic;
			public byte lfUnderline;
			public byte lfStrikeOut;
			public byte lfCharSet;
			public byte lfOutPrecision;
			public byte lfClipPrecision;
			public byte lfQuality;
			public byte lfPitchAndFamily;

			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
			public string lfFaceName;
		}

		private static readonly int NONCLIENTMETRICSSize;

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
		public struct NONCLIENTMETRICS
		{
			public int cbSize;
			public int iBorderWidth;
			public int iScrollWidth;
			public int iScrollHeight;
			public int iCaptionWidth;
			public int iCaptionHeight;
			public LOGFONT lfCaptionFont;
			public int iSMCaptionWidth;
			public int iSMCaptionHeight;
			public LOGFONT lfSMCaptionFont;
			public int iMenuWidth;
			public int iMenuHeight;
			public LOGFONT lfMenuFont;
			public LOGFONT lfStatusFont;
			public LOGFONT lfMessageFont;
			public int iPaddedBorderWidth;

			public static NONCLIENTMETRICS Default { get { return new NONCLIENTMETRICS { cbSize = NONCLIENTMETRICSSize }; } }
		}

		#endregion

		#region GetMonitorInfo/MonitorFromRect/MonitorFromWindow

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetMonitorInfo(HMONITOR hMonitor, [In, Out] ref MONITORINFO lpmi);

		public enum MONITORINFOF : uint
		{
			MONITORINFOF_PRIMARY = 1
		}

		private static readonly int MONITORINFOSize = Marshal.SizeOf(typeof(MONITORINFO));

		[StructLayout(LayoutKind.Sequential)]
		public struct MONITORINFO
		{
			/// <summary>
			/// The size, in bytes, of the structure. Set this member to sizeof(MONITORINFO) (40) before calling the GetMonitorInfo function.
			/// Doing so lets the function determine the type of structure you are passing to it.
			/// </summary>
			public int cbSize;

			/// <summary>
			/// A RECT structure that specifies the display monitor rectangle, expressed in virtual-screen coordinates.
			/// Note that if the monitor is not the primary display monitor, some of the rectangle's coordinates may be negative values.
			/// </summary>
			public RECT rcMonitor;

			/// <summary>
			/// A RECT structure that specifies the work area rectangle of the display monitor that can be used by applications,
			/// expressed in virtual-screen coordinates. Windows uses this rectangle to maximize an application on the monitor.
			/// The rest of the area in rcMonitor contains system windows such as the task bar and side bars.
			/// Note that if the monitor is not the primary display monitor, some of the rectangle's coordinates may be negative values.
			/// </summary>
			public RECT rcWork;

			/// <summary>
			/// The attributes of the display monitor.
			///
			/// This member can be the following value:
			///   1 : MONITORINFOF_PRIMARY
			/// </summary>
			public MONITORINFOF dwFlags;

			public static MONITORINFO Default { get { return new MONITORINFO { cbSize = MONITORINFOSize }; } }
		}

		[DllImport("user32.dll")]
		public static extern HMONITOR MonitorFromRect([In] ref RECT lprc, MFRF dwFlags);

		[DllImport("user32.dll")]
		public static extern HMONITOR MonitorFromWindow(HWND hwnd, MFRF dwFlags);

		public enum MFRF : uint
		{
			MONITOR_MONITOR_DEFAULTTONULL = 0x00000000,
			MONITOR_MONITOR_DEFAULTTOPRIMARY = 0x00000001,
			MONITOR_DEFAULTTONEAREST = 0x00000002
		}

		#endregion

		#region Extension Methods

		public static void ForEach<T>(this System.Collections.Generic.IEnumerable<T> items, Action<T> action)
		{
			foreach (var item in items)
			{
				action(item);
			}
		}

		public static System.Collections.Generic.IEnumerable<T> Unless<T>(this System.Collections.Generic.IEnumerable<T> items, Predicate<T> predicate)
		{
			foreach (var item in items)
			{
				if (!predicate(item))
				{
					yield return item;
				}
			}
		}

		#endregion
	}
}
