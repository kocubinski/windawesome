using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Windawesome
{
	public class ShortcutsManager : IPlugin
	{
		private class Subscription
		{
			private readonly Keys key;

			private readonly bool hasShift;
			private readonly bool hasAlt;
			private readonly bool hasWin;
			private readonly bool hasControl;

			private readonly KeyModifiers shift;
			private readonly KeyModifiers alt;
			private readonly KeyModifiers win;
			private readonly KeyModifiers control;

			public Subscription(KeyModifiers modifiers, Keys key)
			{
				this.key = key;

				shift = modifiers & KeyModifiers.Shift;
				alt = modifiers & KeyModifiers.Alt;
				win = modifiers & KeyModifiers.Win;
				control = modifiers & KeyModifiers.Control;

				hasShift = shift != KeyModifiers.None;
				hasAlt = alt != KeyModifiers.None;
				hasWin = win != KeyModifiers.None;
				hasControl = control != KeyModifiers.None;
			}

			public class SubscriptionEqualityComparer : IEqualityComparer<Subscription>
			{
				#region IEqualityComparer<Subscription> Members

				bool IEqualityComparer<Subscription>.Equals(Subscription x, Subscription y)
				{
					if ((x.hasAlt || y.hasAlt) && (x.alt & y.alt) == 0)
					{
						return false;
					}
					if ((x.hasControl || y.hasControl) && (x.control & y.control) == 0)
					{
						return false;
					}
					if ((x.hasShift || y.hasShift) && (x.shift & y.shift) == 0)
					{
						return false;
					}
					if ((x.hasWin || y.hasWin) && (x.win & y.win) == 0)
					{
						return false;
					}
					return x.key == y.key;
				}

				int IEqualityComparer<Subscription>.GetHashCode(Subscription obj)
				{
					var modifiers = 0;
					if (obj.hasControl)
					{
						modifiers += 1;
					}
					if (obj.hasAlt)
					{
						modifiers += 2;
					}
					if (obj.hasShift)
					{
						modifiers += 4;
					}
					if (obj.hasWin)
					{
						modifiers += 8;
					}

					return modifiers + 256 + (int) obj.key;
				}

				#endregion
			}
		}

		[Flags]
		public enum KeyModifiers
		{
			None = 0,

			LControl = 1,
			RControl = 2,
			Control = LControl | RControl,

			LShift = 4,
			RShift = 8,
			Shift = LShift | RShift,

			LAlt = 16,
			RAlt = 32,
			Alt = LAlt | RAlt,

			LWin = 64,
			RWin = 128,
			Win = LWin | RWin
		}

		public delegate bool KeyboardEventHandler();
		private static readonly NativeMethods.HookProc keyboardHookProcedure = KeyboardHookProc;
		private static readonly List<KeyboardEventHandler> registeredHotkeys;
		private static IntPtr hook;
		private static Dictionary<Subscription, KeyboardEventHandler> subscriptions;
		private static bool hasKeyOnlySubscriptions;

		static ShortcutsManager()
		{
			registeredHotkeys = new List<KeyboardEventHandler>();
		}

		public static void Subscribe(KeyModifiers modifiers, Keys key, KeyboardEventHandler handler)
		{
			if ((modifiers & KeyModifiers.Control) == KeyModifiers.LControl ||
				(modifiers & KeyModifiers.Control) == KeyModifiers.RControl ||
				(modifiers & KeyModifiers.Alt) == KeyModifiers.LAlt ||
				(modifiers & KeyModifiers.Alt) == KeyModifiers.RAlt ||
				(modifiers & KeyModifiers.Shift) == KeyModifiers.LShift ||
				(modifiers & KeyModifiers.Shift) == KeyModifiers.RShift ||
				(modifiers & KeyModifiers.Win) == KeyModifiers.LWin ||
				(modifiers & KeyModifiers.Win) == KeyModifiers.RWin)
			{
				if (hook == IntPtr.Zero)
				{
					subscriptions = new Dictionary<Subscription, KeyboardEventHandler>(new Subscription.SubscriptionEqualityComparer());

					hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, keyboardHookProcedure,
						System.Diagnostics.Process.GetCurrentProcess().MainModule.BaseAddress, 0);
				}

				var newSubscription = new Subscription(modifiers, key);
				KeyboardEventHandler handlers;
				if (subscriptions.TryGetValue(newSubscription, out handlers))
				{
					subscriptions[newSubscription] = handlers + handler;
				}
				else
				{
					subscriptions[newSubscription] = handler;
				}
			}
			else
			{
				NativeMethods.MOD mods = 0;
				if (modifiers.HasFlag(KeyModifiers.Control))
				{
					mods |= NativeMethods.MOD.MOD_CONTROL;
				}
				if (modifiers.HasFlag(KeyModifiers.Alt))
				{
					mods |= NativeMethods.MOD.MOD_ALT;
				}
				if (modifiers.HasFlag(KeyModifiers.Shift))
				{
					mods |= NativeMethods.MOD.MOD_SHIFT;
				}
				if (modifiers.HasFlag(KeyModifiers.Win))
				{
					mods |= NativeMethods.MOD.MOD_WIN;
				}
				NativeMethods.RegisterHotKey(Windawesome.HandleStatic, registeredHotkeys.Count, mods, key);
				registeredHotkeys.Add(handler);
			}

			hasKeyOnlySubscriptions |= modifiers == KeyModifiers.None;
		}

		private static IntPtr KeyboardHookProc(int nCode, UIntPtr wParam, IntPtr lParam)
		{
			if (nCode >= 0)
			{
				if (wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN)
				{
					KeyModifiers modifiersPressed = 0;
					// there is no other way to distinguish between left and right modifier keys
					if ((NativeMethods.GetKeyState(Keys.LShiftKey) & 0x8000) == 0x8000)
					{
						modifiersPressed |= KeyModifiers.LShift;
					}
					if ((NativeMethods.GetKeyState(Keys.RShiftKey) & 0x8000) == 0x8000)
					{
						modifiersPressed |= KeyModifiers.RShift;
					}
					if ((NativeMethods.GetKeyState(Keys.LMenu) & 0x8000) == 0x8000)
					{
						modifiersPressed |= KeyModifiers.LAlt;
					}
					if ((NativeMethods.GetKeyState(Keys.RMenu) & 0x8000) == 0x8000)
					{
						modifiersPressed |= KeyModifiers.RAlt;
					}
					if ((NativeMethods.GetKeyState(Keys.LControlKey) & 0x8000) == 0x8000)
					{
						modifiersPressed |= KeyModifiers.LControl;
					}
					if ((NativeMethods.GetKeyState(Keys.RControlKey) & 0x8000) == 0x8000)
					{
						modifiersPressed |= KeyModifiers.RControl;
					}
					if ((NativeMethods.GetKeyState(Keys.LWin) & 0x8000) == 0x8000)
					{
						modifiersPressed |= KeyModifiers.LWin;
					}
					if ((NativeMethods.GetKeyState(Keys.RWin) & 0x8000) == 0x8000)
					{
						modifiersPressed |= KeyModifiers.RWin;
					}

					if (hasKeyOnlySubscriptions || modifiersPressed != KeyModifiers.None)
					{
						var key = (Keys) Marshal.ReadInt32(lParam);

						KeyboardEventHandler handlers;
						if (subscriptions.TryGetValue(new Subscription(modifiersPressed, key), out handlers))
						{
							if (handlers.GetInvocationList().Cast<KeyboardEventHandler>().Any(handler => handler()))
							{
								return NativeMethods.IntPtrOne;
							}
						}
					}
				}
			}

			return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
		}

		#region IPlugin Members

		void IPlugin.InitializePlugin(Windawesome windawesome)
		{
			windawesome.RegisterMessage(NativeMethods.WM_HOTKEY,
				(ref Message m) => registeredHotkeys[m.WParam.ToInt32()]());
		}

		void IPlugin.Dispose()
		{
			if (hook != IntPtr.Zero)
			{
				NativeMethods.UnhookWindowsHookEx(hook);
			}
			Enumerable.Range(0, registeredHotkeys.Count).ForEach(i => NativeMethods.UnregisterHotKey(Windawesome.HandleStatic, i));
		}

		#endregion
	}
}
