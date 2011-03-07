using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Windawesome
{
	public class ShortcutsManager : IPlugin
	{
		private class Subscription
		{
			private readonly KeyModifiers modifiers;
			private readonly Keys key;

			private readonly bool hasShift;
			private readonly bool hasAlt;
			private readonly bool hasWin;
			private readonly bool hasControl;

			private readonly KeyModifiers shift;
			private readonly KeyModifiers alt;
			private readonly KeyModifiers win;
			private readonly KeyModifiers control;

			internal Subscription(KeyModifiers modifiers, Keys key)
			{
				this.modifiers = modifiers;
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

			internal class SubscriptionEqualityComparer : IEqualityComparer<Subscription>
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
					if (x.key != y.key)
					{
						return false;
					}

					return true;
				}

				int IEqualityComparer<Subscription>.GetHashCode(Subscription obj)
				{
					int modifiers = 0;
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
		private static IntPtr hook = IntPtr.Zero;
		private static readonly NativeMethods.HookProc hookProc;
		private static readonly Dictionary<Subscription, KeyboardEventHandler> subscriptions;
		private static bool hasKeyOnlySubscriptions;
		private static readonly List<KeyboardEventHandler> registeredHotkeys;

		static ShortcutsManager()
		{
			hookProc = KeyboardHookProc;
			subscriptions = new Dictionary<Subscription, KeyboardEventHandler>(new Subscription.SubscriptionEqualityComparer());
			registeredHotkeys = new List<KeyboardEventHandler>();

			hasKeyOnlySubscriptions = false;
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
					hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, hookProc,
						System.Diagnostics.Process.GetCurrentProcess().MainModule.BaseAddress, 0);
				}

				var newSubscription = new Subscription(modifiers, key);
				KeyboardEventHandler handlers;
				if (subscriptions.TryGetValue(newSubscription, out handlers))
				{
					handlers += handler;
				}
				else
				{
					subscriptions[newSubscription] = handler;
				}
			}
			else
			{
				NativeMethods.MOD mods = 0;
				if ((modifiers & KeyModifiers.Control) != 0)
				{
					mods |= NativeMethods.MOD.MOD_CONTROL;
				}
				if ((modifiers & KeyModifiers.Alt) != 0)
				{
					mods |= NativeMethods.MOD.MOD_ALT;
				}
				if ((modifiers & KeyModifiers.Shift) != 0)
				{
					mods |= NativeMethods.MOD.MOD_SHIFT;
				}
				if ((modifiers & KeyModifiers.Win) != 0)
				{
					mods |= NativeMethods.MOD.MOD_WIN;
				}
				NativeMethods.RegisterHotKey(Windawesome.handle, registeredHotkeys.Count, mods, key);
				registeredHotkeys.Add(handler);
			}

			if (modifiers == KeyModifiers.None)
			{
				hasKeyOnlySubscriptions = true;
			}
		}

		private static IntPtr KeyboardHookProc(int nCode, UIntPtr wParam, IntPtr lParam)
		{
			if (nCode >= 0)
			{
				if (wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN)
				{
					KeyModifiers modifiersPressed = 0;
					// there is no other way to distinguish between left and right modifier keys
					if ((NativeMethods.GetKeyState(Keys.LShiftKey) & 0x80) == 0x80)
					{
						modifiersPressed |= KeyModifiers.LShift;
					}
					if ((NativeMethods.GetKeyState(Keys.RShiftKey) & 0x80) == 0x80)
					{
						modifiersPressed |= KeyModifiers.RShift;
					}
					if ((NativeMethods.GetKeyState(Keys.LMenu) & 0x80) == 0x80)
					{
						modifiersPressed |= KeyModifiers.LAlt;
					}
					if ((NativeMethods.GetKeyState(Keys.RMenu) & 0x80) == 0x80)
					{
						modifiersPressed |= KeyModifiers.RAlt;
					}
					if ((NativeMethods.GetKeyState(Keys.LControlKey) & 0x80) == 0x80)
					{
						modifiersPressed |= KeyModifiers.LControl;
					}
					if ((NativeMethods.GetKeyState(Keys.RControlKey) & 0x80) == 0x80)
					{
						modifiersPressed |= KeyModifiers.RControl;
					}
					if ((NativeMethods.GetKeyState(Keys.LWin) & 0x80) == 0x80)
					{
						modifiersPressed |= KeyModifiers.LWin;
					}
					if ((NativeMethods.GetKeyState(Keys.RWin) & 0x80) == 0x80)
					{
						modifiersPressed |= KeyModifiers.RWin;
					}

					if (hasKeyOnlySubscriptions || modifiersPressed != KeyModifiers.None)
					{
						var key = (Keys) Marshal.ReadInt32(lParam);

						KeyboardEventHandler handlers;
						if (subscriptions.TryGetValue(new Subscription(modifiersPressed, key), out handlers))
						{
							foreach (KeyboardEventHandler handler in handlers.GetInvocationList())
							{
								if (handler())
								{
									return NativeMethods.IntPtrOne;
								}
							}
						}
					}
				}
			}

			return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
		}

		#region IPlugin Members

		void IPlugin.InitializePlugin(Windawesome windawesome, Config config)
		{
			Windawesome.RegisterMessage(NativeMethods.WM_HOTKEY,
				(ref Message m) => registeredHotkeys[m.WParam.ToInt32()]());
		}

		void IPlugin.Dispose()
		{
			if (hook != IntPtr.Zero)
			{
				NativeMethods.UnhookWindowsHookEx(hook);
			}
			for (int i = 0; i < registeredHotkeys.Count; i++)
			{
				NativeMethods.UnregisterHotKey(Windawesome.handle, i);
			}
		}

		#endregion
	}
}
