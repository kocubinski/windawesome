using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Windawesome
{
	public sealed class ShortcutsManager : IPlugin
	{
		private sealed class Subscription
		{
			private readonly Keys key;
			private readonly KeyModifiers modifiers;

			public Subscription(KeyModifiers modifiers, Keys key)
			{
				this.key = key;
				this.modifiers = modifiers;
			}

			public sealed class SubscriptionEqualityComparer : IEqualityComparer<Subscription>
			{
				#region IEqualityComparer<Subscription> Members

				bool IEqualityComparer<Subscription>.Equals(Subscription x, Subscription y)
				{
					var xAlt = x.modifiers & KeyModifiers.Alt;
					var yAlt = y.modifiers & KeyModifiers.Alt;
					if ((xAlt != KeyModifiers.None || yAlt != KeyModifiers.None) && (xAlt & yAlt) == 0)
					{
						return false;
					}
					var xControl = x.modifiers & KeyModifiers.Control;
					var yControl = y.modifiers & KeyModifiers.Control;
					if ((xControl != KeyModifiers.None || yControl != KeyModifiers.None) && (xControl & yControl) == 0)
					{
						return false;
					}
					var xShift = x.modifiers & KeyModifiers.Shift;
					var yShift = y.modifiers & KeyModifiers.Shift;
					if ((xShift != KeyModifiers.None || yShift != KeyModifiers.None) && (xShift & yShift) == 0)
					{
						return false;
					}
					var xWin = x.modifiers & KeyModifiers.Win;
					var yWin = y.modifiers & KeyModifiers.Win;
					if ((xWin != KeyModifiers.None || yWin != KeyModifiers.None) && (xWin & yWin) == 0)
					{
						return false;
					}
					return x.key == y.key;
				}

				int IEqualityComparer<Subscription>.GetHashCode(Subscription obj)
				{
					var modifiers = 0;
					if ((obj.modifiers & KeyModifiers.Alt) != KeyModifiers.None)
					{
						modifiers += 1;
					}
					if ((obj.modifiers & KeyModifiers.Control) != KeyModifiers.None)
					{
						modifiers += 2;
					}
					if ((obj.modifiers & KeyModifiers.Shift) != KeyModifiers.None)
					{
						modifiers += 4;
					}
					if ((obj.modifiers & KeyModifiers.Win) != KeyModifiers.None)
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
			if (nCode == 0)
			{
				if (wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN)
				{
					var key = (Keys) Marshal.ReadInt32(lParam);
					if (key != Keys.LShiftKey && key != Keys.RShiftKey &&
						key != Keys.LMenu && key != Keys.RMenu &&
						key != Keys.LControlKey && key != Keys.RControlKey &&
						key != Keys.LWin && key != Keys.RWin)
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
