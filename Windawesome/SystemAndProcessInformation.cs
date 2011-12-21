using System;
using System.Drawing;
using System.Windows.Forms;

namespace Windawesome
{
	public static class SystemAndProcessInformation
	{
		public static readonly bool isRunningElevated;
		public static readonly bool isAtLeastVista;
		public static readonly bool isAtLeast7;
		public static readonly Size smallIconSize;
		public static readonly IntPtr taskbarButtonsWindowHandle;
		public static readonly IntPtr taskbarHandle;
		public static readonly IntPtr startButtonHandle;
		public static readonly IntPtr trayHandle;
		public static readonly IntPtr hiddenTrayHandle;

		static SystemAndProcessInformation()
		{
			isAtLeastVista = Environment.OSVersion.Version.Major >= 6;
			isAtLeast7 = isAtLeastVista && Environment.OSVersion.Version.Minor >= 1;

			isRunningElevated = NativeMethods.IsCurrentProcessElevatedInRespectToShell();

			smallIconSize = SystemInformation.SmallIconSize;

			taskbarButtonsWindowHandle = taskbarHandle;
			taskbarButtonsWindowHandle = NativeMethods.FindWindowEx(taskbarButtonsWindowHandle, IntPtr.Zero, "ReBarWindow32", "");
			taskbarButtonsWindowHandle = NativeMethods.FindWindowEx(taskbarButtonsWindowHandle, IntPtr.Zero, "MSTaskSwWClass", "Running Applications");

			taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
			if (isAtLeastVista)
			{
				startButtonHandle = NativeMethods.FindWindow("Button", "Start");
			}

			trayHandle = FindTrayHandle();
			if (isAtLeast7)
			{
				hiddenTrayHandle = FindHiddenTrayHandle();
			}
		}

		private static IntPtr FindTrayHandle()
		{
			var hWnd = NativeMethods.FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);
			hWnd = NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "SysPager", null);
			return NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "ToolbarWindow32", null);
		}

		private static IntPtr FindHiddenTrayHandle()
		{
			var hWnd = NativeMethods.FindWindow("NotifyIconOverflowWindow", null);
			return NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "ToolbarWindow32", null);
		}
	}
}
