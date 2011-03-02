using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Windawesome
{
	public class KeyboardHook
	{
		private class Subscription
		{
			private readonly KeyModifiers modifiers;
			private readonly Keys key;
			private readonly KeyboardEventHandler handler;

			private readonly bool hasShift;
			private readonly bool hasAlt;
			private readonly bool hasWin;
			private readonly bool hasControl;

			private readonly KeyModifiers shift;
			private readonly KeyModifiers alt;
			private readonly KeyModifiers win;
			private readonly KeyModifiers control;

			internal Subscription(KeyModifiers modifiers, Keys key, KeyboardEventHandler handler)
			{
				this.modifiers = modifiers;
				this.key = key;
				this.handler = handler;

				shift = modifiers & KeyModifiers.Shift;
				alt = modifiers & KeyModifiers.Alt;
				win = modifiers & KeyModifiers.Win;
				control = modifiers & KeyModifiers.Control;

				hasShift = shift != KeyModifiers.None;
				hasAlt = alt != KeyModifiers.None;
				hasWin = win != KeyModifiers.None;
				hasControl = control != KeyModifiers.None;
			}

			internal KeyboardEventHandler GetHandler()
			{
				return handler;
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
		private static readonly IntPtr hook;
		private static readonly NativeMethods.HookProc hookProc;
		private static readonly Dictionary<Subscription, KeyboardEventHandler> subscriptions;
		private static bool hasKeyOnlySubscriptions;

		static KeyboardHook()
		{
			hookProc = KeyboardHookProc;
			subscriptions = new Dictionary<Subscription, KeyboardEventHandler>(new Subscription.SubscriptionEqualityComparer());
			hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, hookProc,
				System.Diagnostics.Process.GetCurrentProcess().MainModule.BaseAddress, 0);

			hasKeyOnlySubscriptions = false;
		}

		internal static void Dispose()
		{
			NativeMethods.UnhookWindowsHookEx(hook);
		}

		public static void Subscribe(KeyModifiers modifiers, Keys key, KeyboardEventHandler handler)
		{
			var newSubscription = new Subscription(modifiers, key, handler);
			KeyboardEventHandler handlers;
			if (subscriptions.TryGetValue(newSubscription, out handlers))
			{
				handlers += handler;
			}
			else
			{
				subscriptions[newSubscription] = handler;
			}

			if (modifiers == KeyModifiers.None)
			{
				hasKeyOnlySubscriptions = true;
			}
		}

		private static KeyModifiers modifiersPressed = KeyModifiers.None;
		private static IntPtr KeyboardHookProc(int nCode, UIntPtr wParam, IntPtr lParam)
		{
			if (nCode >= 0)
			{
				var key = (Keys) Marshal.ReadInt32(lParam);

				if (wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN)
				{
					switch (key)
					{
						case Keys.LShiftKey:
							modifiersPressed |= KeyModifiers.LShift;
							break;
						case Keys.RShiftKey:
							modifiersPressed |= KeyModifiers.RShift;
							break;
						case Keys.LControlKey:
							modifiersPressed |= KeyModifiers.LControl;
							break;
						case Keys.RControlKey:
							modifiersPressed |= KeyModifiers.RControl;
							break;
						case Keys.LMenu:
							modifiersPressed |= KeyModifiers.LAlt;
							break;
						case Keys.RMenu:
							modifiersPressed |= KeyModifiers.RAlt;
							break;
						case Keys.LWin:
							modifiersPressed |= KeyModifiers.LWin;
							break;
						case Keys.RWin:
							modifiersPressed |= KeyModifiers.RWin;
							break;
						default:
							if (hasKeyOnlySubscriptions || modifiersPressed != KeyModifiers.None)
							{
								var newSubscription = new Subscription(modifiersPressed, key, null);
								KeyboardEventHandler handlers;
								if (subscriptions.TryGetValue(newSubscription, out handlers))
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
							break;
					}
				}
				else
				{
					switch (key)
					{
						case Keys.LShiftKey:
							modifiersPressed &= ~KeyModifiers.LShift;
							break;
						case Keys.RShiftKey:
							modifiersPressed &= ~KeyModifiers.RShift;
							break;
						case Keys.LMenu:
							modifiersPressed &= ~KeyModifiers.LAlt;
							break;
						case Keys.RMenu:
							modifiersPressed &= ~KeyModifiers.RAlt;
							break;
						case Keys.LControlKey:
							modifiersPressed &= ~KeyModifiers.LControl;
							break;
						case Keys.RControlKey:
							modifiersPressed &= ~KeyModifiers.RControl;
							break;
						case Keys.LWin:
							modifiersPressed &= ~KeyModifiers.LWin;
							break;
						case Keys.RWin:
							modifiersPressed &= ~KeyModifiers.RWin;
							break;
					}
				}
			}

			return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
		}
	}
}
