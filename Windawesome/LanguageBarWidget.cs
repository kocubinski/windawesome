using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Windawesome
{
	public sealed class LanguageBarWidget : IFixedWidthWidget
	{
		private uint globalShellHookMessage;
		private Bar bar;

		private Label label;
		private bool isLeft;
		private readonly Color backgroundColor;
		private readonly Color foregroundColor;
		private readonly static StringBuilder stringBuilder = new StringBuilder(85);

		private delegate void InputLanguageChangedEventHandler(string language);
		private static event InputLanguageChangedEventHandler InputLanguageChanged;

		public LanguageBarWidget(Color? backgroundColor = null, Color? foregroundColor = null)
		{
			this.backgroundColor = backgroundColor ?? Color.White;
			this.foregroundColor = foregroundColor ?? Color.Black;
		}

		private static bool OnGlobalShellHookMessage(ref Message m)
		{
			InputLanguageChanged(GetWindowKeyboardLanguage(NativeMethods.GetForegroundWindow()));
			return true;
		}

		private static string GetWindowKeyboardLanguage(IntPtr hWnd)
		{
			var keyboardLayout = NativeMethods.GetKeyboardLayout(
				NativeMethods.GetWindowThreadProcessId(hWnd, IntPtr.Zero));

			var localeId = unchecked((uint) (short) keyboardLayout.ToInt32());

			if (SystemAndProcessInformation.isAtLeastVista)
			{
				NativeMethods.LCIDToLocaleName(localeId, stringBuilder,
					stringBuilder.Capacity, 0);

				return stringBuilder.ToString();
			}

			// XP doesn't have LCIDToLocaleName
			NativeMethods.GetLocaleInfo(localeId, NativeMethods.LOCALE_SISO639LANGNAME, stringBuilder, stringBuilder.Capacity);
			var languageName = stringBuilder.ToString();
			NativeMethods.GetLocaleInfo(localeId, NativeMethods.LOCALE_SISO3166CTRYNAME, stringBuilder, stringBuilder.Capacity);
			return languageName + "-" + stringBuilder;
		}

		private void SetNewLanguage(string language)
		{
			if (language != label.Text)
			{
				var oldLeft = label.Left;
				var oldRight = label.Right;
				var oldWidth = label.Width;
				label.Text = language;

				label.Width = TextRenderer.MeasureText(label.Text, label.Font).Width;
				if (oldWidth != label.Width)
				{
					this.RepositionControls(oldLeft, oldRight);
					bar.DoFixedWidthWidgetWidthChanged(this);
				}
			}
		}

		private void OnWindowActivatedEvent(IntPtr hWnd)
		{
			if (bar.Monitor.CurrentVisibleWorkspace.IsCurrentWorkspace)
			{
				SetNewLanguage(GetWindowKeyboardLanguage(hWnd));
			}
		}

		#region IFixedWidthWidget Members

		void IWidget.StaticInitializeWidget(Windawesome windawesome)
		{
			if (NativeMethods.RegisterGlobalShellHook(windawesome.Handle))
			{
				globalShellHookMessage = NativeMethods.RegisterWindowMessage("GLOBAL_SHELL_HOOK");
				if (SystemAndProcessInformation.isAtLeastVista && SystemAndProcessInformation.isRunningElevated)
				{
					if (SystemAndProcessInformation.isAtLeast7)
					{
						NativeMethods.ChangeWindowMessageFilterEx(windawesome.Handle, globalShellHookMessage, NativeMethods.MSGFLTEx.MSGFLT_ALLOW, IntPtr.Zero);
					}
					else
					{
						NativeMethods.ChangeWindowMessageFilter(globalShellHookMessage, NativeMethods.MSGFLT.MSGFLT_ADD);
					}
				}

				windawesome.RegisterMessage((int) globalShellHookMessage, OnGlobalShellHookMessage);
			}
		}

		void IWidget.InitializeWidget(Bar bar)
		{
			this.bar = bar;

			label = bar.CreateLabel("", 0);
			label.BackColor = backgroundColor;
			label.ForeColor = foregroundColor;
			label.TextAlign = ContentAlignment.MiddleCenter;

			Workspace.WindowActivatedEvent += OnWindowActivatedEvent;
			InputLanguageChanged += SetNewLanguage;
		}

		IEnumerable<Control> IFixedWidthWidget.GetInitialControls(bool isLeft)
		{
			this.isLeft = isLeft;

			return new[] { label };
		}

		public void RepositionControls(int left, int right)
		{
			this.label.Location = this.isLeft ? new Point(left, 0) : new Point(right - this.label.Width, 0);
		}

		int IWidget.GetLeft()
		{
			return label.Left;
		}

		int IWidget.GetRight()
		{
			return label.Right;
		}

		void IWidget.StaticDispose()
		{
			NativeMethods.UnregisterGlobalShellHook();

			// remove the message filters
			if (SystemAndProcessInformation.isAtLeastVista && SystemAndProcessInformation.isRunningElevated)
			{
				if (SystemAndProcessInformation.isAtLeast7)
				{
					NativeMethods.ChangeWindowMessageFilterEx(Windawesome.HandleStatic, globalShellHookMessage, NativeMethods.MSGFLTEx.MSGFLT_RESET, IntPtr.Zero);
				}
				else
				{
					NativeMethods.ChangeWindowMessageFilter(globalShellHookMessage, NativeMethods.MSGFLT.MSGFLT_REMOVE);
				}
			}
		}

		void IWidget.Dispose()
		{
		}

		void IWidget.Refresh()
		{
		}

		#endregion
	}
}
