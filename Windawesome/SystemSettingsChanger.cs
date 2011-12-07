using System;

namespace Windawesome
{
	internal static class SystemSettingsChanger
	{
		private static readonly NativeMethods.NONCLIENTMETRICS originalNonClientMetrics;
		private static readonly NativeMethods.ANIMATIONINFO originalAnimationInfo;
		private static readonly bool originalHideMouseWhenTyping;
		private static readonly bool originalFocusFollowsMouse;
		private static readonly bool originalFocusFollowsMouseSetOnTop;

		static SystemSettingsChanger()
		{
			originalNonClientMetrics = NativeMethods.NONCLIENTMETRICS.Default;
			NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETNONCLIENTMETRICS, originalNonClientMetrics.cbSize,
				ref originalNonClientMetrics, 0);

			originalAnimationInfo = NativeMethods.ANIMATIONINFO.Default;
			NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETANIMATION, originalAnimationInfo.cbSize,
				ref originalAnimationInfo, 0);

			NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETMOUSEVANISH, 0,
				ref originalHideMouseWhenTyping, 0);

			NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETACTIVEWINDOWTRACKING, 0,
				ref originalFocusFollowsMouse, 0);

			NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETACTIVEWNDTRKZORDER, 0,
				ref originalFocusFollowsMouseSetOnTop, 0);
		}

		public static void ApplyChanges(Config config)
		{
			System.Threading.Tasks.Task.Factory.StartNew(() =>
			{
				// set the "hide mouse when typing"
				if (config.HideMouseWhenTyping != originalHideMouseWhenTyping)
				{
					var hideMouseWhenTyping = config.HideMouseWhenTyping;
					NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETMOUSEVANISH, 0,
						ref hideMouseWhenTyping, 0);

					NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
						(UIntPtr) (uint) NativeMethods.SPI.SPI_SETMOUSEVANISH, IntPtr.Zero);
				}

				// set the "focus follows mouse"
				if (config.FocusFollowsMouse != originalFocusFollowsMouse)
				{
					var focusFollowsMouse = config.FocusFollowsMouse;
					NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWINDOWTRACKING, 0,
						ref focusFollowsMouse, 0);

					NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
						(UIntPtr) (uint) NativeMethods.SPI.SPI_SETACTIVEWINDOWTRACKING, IntPtr.Zero);
				}

				// set the "set window on top on focus follows mouse"
				if (config.FocusFollowsMouseSetOnTop != originalFocusFollowsMouseSetOnTop)
				{
					var focusFollowsMouseSetOnTop = config.FocusFollowsMouseSetOnTop;
					NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWNDTRKZORDER, 0,
						ref focusFollowsMouseSetOnTop, 0);

					NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
						(UIntPtr) (uint) NativeMethods.SPI.SPI_SETACTIVEWNDTRKZORDER, IntPtr.Zero);
				}

				// set the minimize/maximize/restore animations
				if ((originalAnimationInfo.iMinAnimate == 1 && !config.ShowMinimizeMaximizeRestoreAnimations) ||
					(originalAnimationInfo.iMinAnimate == 0 && config.ShowMinimizeMaximizeRestoreAnimations))
				{
					var animationInfo = originalAnimationInfo;
					animationInfo.iMinAnimate = config.ShowMinimizeMaximizeRestoreAnimations ? 1 : 0;
					NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETANIMATION, animationInfo.cbSize,
						ref animationInfo, 0);

					NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
						(UIntPtr) (uint) NativeMethods.SPI.SPI_SETANIMATION, IntPtr.Zero);
				}

				// set the global border and padded border widths
				if ((config.WindowBorderWidth >= 0 && originalNonClientMetrics.iBorderWidth != config.WindowBorderWidth) ||
					(Windawesome.isAtLeastVista && config.WindowPaddedBorderWidth >= 0 && originalNonClientMetrics.iPaddedBorderWidth != config.WindowPaddedBorderWidth))
				{
					var metrics = originalNonClientMetrics;
					metrics.iBorderWidth = config.WindowBorderWidth;
					metrics.iPaddedBorderWidth = config.WindowPaddedBorderWidth;
					NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
						ref metrics, 0);

					NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
						(UIntPtr) (uint) NativeMethods.SPI.SPI_SETNONCLIENTMETRICS, IntPtr.Zero);
				}
			});
		}

		public static void RevertChanges(Config config)
		{
			var thread = new System.Threading.Thread(() => // this has to be a foreground thread
			{
				// revert the hiding of the mouse when typing
				if (config.HideMouseWhenTyping != originalHideMouseWhenTyping)
				{
					var hideMouseWhenTyping = originalHideMouseWhenTyping;
					NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETMOUSEVANISH, 0,
						ref hideMouseWhenTyping, NativeMethods.SPIF.SPIF_UPDATEINIFILE);

					NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
						(UIntPtr) (uint) NativeMethods.SPI.SPI_SETMOUSEVANISH, IntPtr.Zero);
				}

				// revert the "focus follows mouse"
				if (config.FocusFollowsMouse != originalFocusFollowsMouse)
				{
					var focusFollowsMouse = originalFocusFollowsMouse;
					NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWINDOWTRACKING, 0,
						ref focusFollowsMouse, NativeMethods.SPIF.SPIF_UPDATEINIFILE);

					NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
						(UIntPtr) (uint) NativeMethods.SPI.SPI_SETACTIVEWINDOWTRACKING, IntPtr.Zero);
				}

				// revert the "set window on top on focus follows mouse"
				if (config.FocusFollowsMouseSetOnTop != originalFocusFollowsMouseSetOnTop)
				{
					var focusFollowsMouseSetOnTop = originalFocusFollowsMouseSetOnTop;
					NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWNDTRKZORDER, 0,
						ref focusFollowsMouseSetOnTop, NativeMethods.SPIF.SPIF_UPDATEINIFILE);

					NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
						(UIntPtr) (uint) NativeMethods.SPI.SPI_SETACTIVEWNDTRKZORDER, IntPtr.Zero);
				}

				// revert the minimize/maximize/restore animations
				if ((originalAnimationInfo.iMinAnimate == 1 && !config.ShowMinimizeMaximizeRestoreAnimations) ||
					(originalAnimationInfo.iMinAnimate == 0 && config.ShowMinimizeMaximizeRestoreAnimations))
				{
					var animationInfo = originalAnimationInfo;
					NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETANIMATION, animationInfo.cbSize,
						ref animationInfo, NativeMethods.SPIF.SPIF_UPDATEINIFILE);

					NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
						(UIntPtr) (uint) NativeMethods.SPI.SPI_SETANIMATION, IntPtr.Zero);
				}

				// revert the size of non-client area of windows
				if ((config.WindowBorderWidth >= 0 && originalNonClientMetrics.iBorderWidth != config.WindowBorderWidth) ||
					(Windawesome.isAtLeastVista && config.WindowPaddedBorderWidth >= 0 && originalNonClientMetrics.iPaddedBorderWidth != config.WindowPaddedBorderWidth))
				{
					var metrics = originalNonClientMetrics;
					NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
						ref metrics, NativeMethods.SPIF.SPIF_UPDATEINIFILE);

					NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
						(UIntPtr) (uint) NativeMethods.SPI.SPI_SETNONCLIENTMETRICS, IntPtr.Zero);
				}
			});
			thread.Start();

			new System.Threading.Timer(_ =>
			{
				// SystemParametersInfo sometimes hangs because of SPI_SETNONCLIENTMETRICS,
				// even though SPIF_SENDCHANGE is not added to the flags
				if (thread.IsAlive)
				{
					thread.Abort();
					Environment.Exit(0);
				}
			}, null, 5000, 0);
		}
	}
}
