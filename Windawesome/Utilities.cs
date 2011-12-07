using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Windawesome
{
	public static class Utilities
	{
		private static readonly NativeMethods.INPUT[] input = new NativeMethods.INPUT[18];

		// sends the hotkey combination without disrupting the currently pressed modifiers
		public static void SendHotkey(Tuple<NativeMethods.MOD, Keys> hotkey)
		{
			uint i = 0;

			// press needed modifiers
			var shiftShouldBePressed = hotkey.Item1.HasFlag(NativeMethods.MOD.MOD_SHIFT);
			var leftShiftPressed = (NativeMethods.GetAsyncKeyState(Keys.LShiftKey) & 0x8000) == 0x8000;
			var rightShiftPressed = (NativeMethods.GetAsyncKeyState(Keys.RShiftKey) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftShiftPressed, rightShiftPressed, shiftShouldBePressed,
				new NativeMethods.INPUT(Keys.ShiftKey, 0),
				new NativeMethods.INPUT(Keys.LShiftKey, NativeMethods.KEYEVENTF_KEYUP),
				new NativeMethods.INPUT(Keys.RShiftKey, NativeMethods.KEYEVENTF_KEYUP), ref i);

			var winShouldBePressed = hotkey.Item1.HasFlag(NativeMethods.MOD.MOD_WIN);
			var leftWinPressed = (NativeMethods.GetAsyncKeyState(Keys.LWin) & 0x8000) == 0x8000;
			var rightWinPressed = (NativeMethods.GetAsyncKeyState(Keys.RWin) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftWinPressed, rightWinPressed, winShouldBePressed,
				new NativeMethods.INPUT(Keys.LWin, 0),
				new NativeMethods.INPUT(Keys.LWin, NativeMethods.KEYEVENTF_KEYUP),
				new NativeMethods.INPUT(Keys.RWin, NativeMethods.KEYEVENTF_KEYUP), ref i);

			var controlShouldBePressed = hotkey.Item1.HasFlag(NativeMethods.MOD.MOD_CONTROL);
			var leftControlPressed = (NativeMethods.GetAsyncKeyState(Keys.LControlKey) & 0x8000) == 0x8000;
			var rightControlPressed = (NativeMethods.GetAsyncKeyState(Keys.RControlKey) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftControlPressed, rightControlPressed, controlShouldBePressed,
				new NativeMethods.INPUT(Keys.ControlKey, 0),
				new NativeMethods.INPUT(Keys.LControlKey, NativeMethods.KEYEVENTF_KEYUP),
				new NativeMethods.INPUT(Keys.RControlKey, NativeMethods.KEYEVENTF_KEYUP), ref i);

			var altShouldBePressed = hotkey.Item1.HasFlag(NativeMethods.MOD.MOD_ALT);
			var leftAltPressed = (NativeMethods.GetAsyncKeyState(Keys.LMenu) & 0x8000) == 0x8000;
			var rightAltPressed = (NativeMethods.GetAsyncKeyState(Keys.RMenu) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftAltPressed, rightAltPressed, altShouldBePressed,
				new NativeMethods.INPUT(Keys.Menu, 0),
				new NativeMethods.INPUT(Keys.LMenu, NativeMethods.KEYEVENTF_KEYUP),
				new NativeMethods.INPUT(Keys.RMenu, NativeMethods.KEYEVENTF_KEYUP), ref i);

			// press and release key
			input[i++] = new NativeMethods.INPUT(hotkey.Item2, 0);
			input[i++] = new NativeMethods.INPUT(hotkey.Item2, NativeMethods.KEYEVENTF_KEYUP);

			// revert changes to modifiers
			PressReleaseModifierKey(leftAltPressed, rightAltPressed, altShouldBePressed,
				new NativeMethods.INPUT(Keys.Menu, NativeMethods.KEYEVENTF_KEYUP),
				new NativeMethods.INPUT(Keys.LMenu, 0),
				new NativeMethods.INPUT(Keys.RMenu, 0), ref i);

			PressReleaseModifierKey(leftControlPressed, rightControlPressed, controlShouldBePressed,
				new NativeMethods.INPUT(Keys.ControlKey, NativeMethods.KEYEVENTF_KEYUP),
				new NativeMethods.INPUT(Keys.LControlKey, 0),
				new NativeMethods.INPUT(Keys.RControlKey, 0), ref i);

			PressReleaseModifierKey(leftWinPressed, rightWinPressed, winShouldBePressed,
				new NativeMethods.INPUT(Keys.LWin, NativeMethods.KEYEVENTF_KEYUP),
				new NativeMethods.INPUT(Keys.LWin, 0),
				new NativeMethods.INPUT(Keys.RWin, 0), ref i);

			PressReleaseModifierKey(leftShiftPressed, rightShiftPressed, shiftShouldBePressed,
				new NativeMethods.INPUT(Keys.ShiftKey, NativeMethods.KEYEVENTF_KEYUP),
				new NativeMethods.INPUT(Keys.LShiftKey, 0),
				new NativeMethods.INPUT(Keys.RShiftKey, 0), ref i);

			NativeMethods.SendInput(i, input, NativeMethods.INPUTSize);
		}

		private static void PressReleaseModifierKey(
			bool leftKeyPressed, bool rightKeyPressed, bool keyShouldBePressed,
			NativeMethods.INPUT action, NativeMethods.INPUT leftAction, NativeMethods.INPUT rightAction, ref uint i)
		{
			if (keyShouldBePressed)
			{
				if (!leftKeyPressed && !rightKeyPressed)
				{
					input[i++] = action;
				}
			}
			else
			{
				if (leftKeyPressed)
				{
					input[i++] = leftAction;
				}
				if (rightKeyPressed)
				{
					input[i++] = rightAction;
				}
			}
		}

		public static void MoveMouseToMiddleOf(Rectangle bounds)
		{
			NativeMethods.SetCursorPos((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);
		}

		public static IntPtr GetRootOwner(IntPtr hWnd)
		{
			var result = hWnd;
			hWnd = NativeMethods.GetWindow(hWnd, NativeMethods.GW.GW_OWNER);
			while (hWnd != IntPtr.Zero && hWnd != result)
			{
				result = hWnd;
				hWnd = NativeMethods.GetWindow(hWnd, NativeMethods.GW.GW_OWNER);
			}
			return result;
		}

		public static IntPtr DoForSelfAndOwnersWhile(IntPtr hWnd, Predicate<IntPtr> action)
		{
			while (hWnd != IntPtr.Zero && action(hWnd))
			{
				hWnd = NativeMethods.GetWindow(hWnd, NativeMethods.GW.GW_OWNER);
			}
			return hWnd;
		}

		// http://blogs.msdn.com/b/oldnewthing/archive/2007/10/08/5351207.aspx
		// http://stackoverflow.com/questions/210504/enumerate-windows-like-alt-tab-does
		public static bool IsAltTabWindow(IntPtr hWnd)
		{
			var exStyle = NativeMethods.GetWindowExStyleLongPtr(hWnd);
			if (exStyle.HasFlag(NativeMethods.WS_EX.WS_EX_TOOLWINDOW) ||
				NativeMethods.GetWindow(hWnd, NativeMethods.GW.GW_OWNER) != IntPtr.Zero)
			{
				return false;
			}
			if (exStyle.HasFlag(NativeMethods.WS_EX.WS_EX_APPWINDOW))
			{
				return true;
			}

			// Start at the root owner
			var hWndTry = NativeMethods.GetAncestor(hWnd, NativeMethods.GA.GA_ROOTOWNER);
			IntPtr oldHWnd;

			// See if we are the last active visible popup
			do
			{
				oldHWnd = hWndTry;
				hWndTry = NativeMethods.GetLastActivePopup(hWndTry);
			}
			while (oldHWnd != hWndTry && !NativeMethods.IsWindowVisible(hWndTry));

			return hWndTry == hWnd;
		}

		public static bool IsAppWindow(IntPtr hWnd)
		{
			return NativeMethods.IsWindowVisible(hWnd) &&
				!NativeMethods.GetWindowExStyleLongPtr(hWnd).HasFlag(NativeMethods.WS_EX.WS_EX_NOACTIVATE) &&
				!NativeMethods.GetWindowStyleLongPtr(hWnd).HasFlag(NativeMethods.WS.WS_CHILD);
		}

		public static bool IsVisibleAndNotHung(Window window)
		{
			return NativeMethods.IsWindowVisible(window.hWnd) && WindowIsNotHung(window.hWnd);
		}

		public static bool WindowIsNotHung(Window window)
		{
			return WindowIsNotHung(window.hWnd);
		}

		public static bool WindowIsNotHung(IntPtr hWnd)
		{
			// IsHungAppWindow is not going to work, as it starts returning true 5 seconds after the window
			// has hung - so if a SetWindowPos, e.g., is called on such a window, it may block forever, even
			// though IsHungAppWindow returned false

			// SendMessageTimeout with a small timeout will timeout for some programs which are heavy on
			// computation and do not respond in time - like Visual Studio. A big timeout works, but if an
			// application is really hung, then Windawesome is blocked for this number of milliseconds.
			// That can be felt and is annoying. Perhaps the best scenario is:
			// return !IsHungAppWindow && (SendMessageTimeout(with_big_timeout) || GetLastWin32Error)
			// As this will not block forever at any point and it will only block the main thread for "with_big_timeout"
			// milliseconds the first 5 seconds when a program is hung - after that IsHungAppWindow catches it
			// immediately and returns. However, I decided that in most cases apps are not hung, so the overhead
			// of calling IsHungAppWindow AND SendMessageTimeout is not worth.

			return NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_NULL, UIntPtr.Zero, IntPtr.Zero,
				NativeMethods.SMTO.SMTO_ABORTIFHUNG | NativeMethods.SMTO.SMTO_BLOCK, 3000, IntPtr.Zero) != IntPtr.Zero;
		}

		public static void QuitApplication(IntPtr hWnd)
		{
			NativeMethods.SendNotifyMessage(hWnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_CLOSE, IntPtr.Zero);
		}

		public static void MinimizeApplication(IntPtr hWnd)
		{
			NativeMethods.SendNotifyMessage(hWnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MINIMIZE, IntPtr.Zero);
		}

		public static void MaximizeApplication(IntPtr hWnd)
		{
			NativeMethods.SendNotifyMessage(hWnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MAXIMIZE, IntPtr.Zero);
		}

		public static void RestoreApplication(IntPtr hWnd)
		{
			NativeMethods.SendNotifyMessage(hWnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_RESTORE, IntPtr.Zero);
		}

		public static void RunApplication(string path, string arguments = "")
		{
			Task.Factory.StartNew(() =>
				{
					if (SystemAndProcessInformation.isAtLeastVista && SystemAndProcessInformation.isRunningElevated)
					{
						NativeMethods.RunApplicationNonElevated(path, arguments); // TODO: this is not working on XP
					}
					else
					{
						Process.Start(path, arguments);
					}
				});
		}

		public static void GetWindowSmallIconAsBitmap(IntPtr hWnd, Action<Bitmap> action)
		{
			IntPtr result;
			NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL,
				IntPtr.Zero, NativeMethods.SMTO.SMTO_BLOCK, 500, out result);

			if (result == IntPtr.Zero)
			{
				NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_QUERYDRAGICON, UIntPtr.Zero,
					IntPtr.Zero, NativeMethods.SMTO.SMTO_BLOCK, 500, out result);
			}

			if (result == IntPtr.Zero)
			{
				result = NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCL_HICONSM);
			}

			if (result == IntPtr.Zero)
			{
				Task.Factory.StartNew(hWnd2 =>
					{
						Bitmap bitmap = null;
						try
						{
							int processId;
							NativeMethods.GetWindowThreadProcessId((IntPtr) hWnd2, out processId);
							var processFileName = Process.GetProcessById(processId).MainModule.FileName;

							var info = new NativeMethods.SHFILEINFO();

							NativeMethods.SHGetFileInfo(processFileName, 0, ref info,
								Marshal.SizeOf(info), NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

							if (info.hIcon != IntPtr.Zero)
							{
								bitmap = new Bitmap(Bitmap.FromHicon(info.hIcon), SystemAndProcessInformation.smallIconSize);
								NativeMethods.DestroyIcon(info.hIcon);
							}
							else
							{
								var icon = Icon.ExtractAssociatedIcon(processFileName);
								if (icon != null)
								{
									bitmap = new Bitmap(icon.ToBitmap(), SystemAndProcessInformation.smallIconSize);
								}
							}
						}
						catch
						{
						}

						return bitmap;
					}, hWnd).ContinueWith(t => action(t.Result), TaskScheduler.FromCurrentSynchronizationContext());
			}
			else
			{
				Bitmap bitmap = null;
				try
				{
					bitmap = new Bitmap(Bitmap.FromHicon(result), SystemAndProcessInformation.smallIconSize);
				}
				catch
				{
				}
				action(bitmap);
			}
		}
	}
}
