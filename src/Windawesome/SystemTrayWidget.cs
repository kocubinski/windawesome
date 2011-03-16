using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Windawesome
{
	public class SystemTrayWidget : IWidget
	{
		private readonly Dictionary<Tuple<int, uint>, Tuple<SystemTray.TrayIcon, PictureBox, ToolTip>> icons; // (hWnd, uID) -> (TrayIcon, PictureBox, ToolTip)

		private Bar bar;
		private int left, right;
		private bool isLeft;

		private static int nonHiddenButtonsCount;

		#region Events

		private delegate void IconAddedEventHandler(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple);
		private static event IconAddedEventHandler IconAdded;

		private delegate void IconModifiedEventHandler(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple);
		private static event IconModifiedEventHandler IconModified;

		private delegate void IconRemovedEventHandler(Tuple<int, uint> tuple);
		private static event IconRemovedEventHandler IconRemoved;

		private delegate void HiddenIconAddedEventHandler(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple);
		private static event HiddenIconAddedEventHandler HiddenIconAdded;

		private static void OnIconAdded(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple)
		{
			IconAdded(iconData, tuple);
		}

		private static void OnIconModified(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple)
		{
			IconModified(iconData, tuple);
		}

		private static void OnIconRemoved(Tuple<int, uint> tuple)
		{
			IconRemoved(tuple);
		}

		private static void OnHiddenIconAdded(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple)
		{
			if (HiddenIconAdded != null)
			{
				HiddenIconAdded(iconData, tuple);
			}
		}

		#endregion

		public SystemTrayWidget(bool showFullSystemTray = false)
		{
			icons = new Dictionary<Tuple<int, uint>, Tuple<SystemTray.TrayIcon, PictureBox, ToolTip>>(20);

			IconAdded	 += SystemTrayWidget_IconAdded;
			IconModified += SystemTrayWidget_IconModified;
			IconRemoved	 += SystemTrayWidget_IconRemoved;

			foreach (var icon in SystemTray.GetButtons(SystemTray.trayHandle))
			{
				var pictureBox = CreatePictureBox(icon);
				var toolTip = CreateToolTip(pictureBox, icon.tooltip);
				icons[new Tuple<int, uint>((int) icon.hWnd, icon.id)] = new Tuple<SystemTray.TrayIcon, PictureBox, ToolTip>(icon, pictureBox, toolTip);
			}

			nonHiddenButtonsCount = icons.Count;

			if (showFullSystemTray)
			{
				HiddenIconAdded	+= SystemTrayWidget_IconAdded;

				if (Windawesome.isAtLeast7)
				{
					foreach (var icon in SystemTray.GetButtons(SystemTray.hiddenTrayHandle))
					{
						var pictureBox = CreatePictureBox(icon);
						var toolTip = CreateToolTip(pictureBox, icon.tooltip);
						icons[new Tuple<int, uint>((int) icon.hWnd, icon.id)] = new Tuple<SystemTray.TrayIcon, PictureBox, ToolTip>(icon, pictureBox, toolTip);
					}
				}
			}
		}

		private void SystemTrayWidget_IconAdded(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple)
		{
			if (!icons.ContainsKey(tuple))
			{
				var trayIcon = new SystemTray.TrayIcon();
				trayIcon.hWnd = (IntPtr) iconData.hWnd;
				trayIcon.id = iconData.uID;
				var pictureBox = CreatePictureBox(trayIcon);
				var t = new Tuple<SystemTray.TrayIcon, PictureBox, ToolTip>(trayIcon, pictureBox, CreateToolTip(pictureBox, trayIcon.tooltip));
				UpdateIconData(t, iconData);
				icons[tuple] = t;
				if (TrayIconVisible(trayIcon))
				{
					RepositionControls(left, right);
					bar.OnWidgetControlsChanged(this, new PictureBox[0], new PictureBox[] { pictureBox });
				}
			}
		}

		private void SystemTrayWidget_IconModified(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple)
		{
			Tuple<SystemTray.TrayIcon, PictureBox, ToolTip> trayIcon;

			if (icons.TryGetValue(tuple, out trayIcon) && UpdateIconData(trayIcon, iconData))
			{
				RepositionControls(left, right);
				if (TrayIconVisible(trayIcon.Item1))
				{
					bar.OnWidgetControlsChanged(this, new PictureBox[0], new PictureBox[] { trayIcon.Item2 });
				}
				else
				{
					bar.OnWidgetControlsChanged(this, new PictureBox[] { trayIcon.Item2 }, new PictureBox[0]);
				}
			}
		}

		private void SystemTrayWidget_IconRemoved(Tuple<int, uint> tuple)
		{
			Tuple<SystemTray.TrayIcon, PictureBox, ToolTip> trayIcon;

			if (icons.TryGetValue(tuple, out trayIcon))
			{
				icons.Remove(tuple);
				RepositionControls(left, right);

				bar.OnWidgetControlsChanged(this, new PictureBox[] { trayIcon.Item2 }, new PictureBox[0]);
			}
		}

		private static bool OnSystemTrayMessage(ref Message m)
		{
			var copyDataStruct = (NativeMethods.COPYDATASTRUCT) Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.COPYDATASTRUCT));

			if (copyDataStruct.dwData == NativeMethods.SH_TRAY_DATA)
			{
				var trayData = (NativeMethods.SHELLTRAYDATA) Marshal.PtrToStructure(copyDataStruct.lpData, typeof(NativeMethods.SHELLTRAYDATA));

				if (trayData.dwHz == 0x34753423) // some Microsoft magic number to indicate a system tray message
				{
					if ((trayData.nid.uFlags & NativeMethods.IconDataMembers.NIF_ICON) != 0 && trayData.dwMessage != NativeMethods.NIM_DELETE)
					{
						trayData.nid.hIcon = NativeMethods.CopyIcon((IntPtr) trayData.nid.hIcon).ToInt32();
					}

					NativeMethods.ReplyMessage(NativeMethods.IntPtrOne);

					switch (trayData.dwMessage)
					{
						case NativeMethods.NIM_ADD:
							TrayIconAdded(trayData.nid);
							break;
						case NativeMethods.NIM_MODIFY:
							TrayIconModified(trayData.nid);
							break;
						case NativeMethods.NIM_DELETE:
							TrayIconDeleted(trayData.nid);
							break;
					}

					if ((trayData.nid.uFlags & NativeMethods.IconDataMembers.NIF_ICON) != 0 && trayData.dwMessage != NativeMethods.NIM_DELETE)
					{
						NativeMethods.DestroyIcon((IntPtr) trayData.nid.hIcon);
					}

					m.Result = NativeMethods.IntPtrOne; // return TRUE
					return true;
				}
			}

			return false;
		}

		private static PictureBox CreatePictureBox(SystemTray.TrayIcon trayIcon)
		{
			var pictureBox = new PictureBox();
			pictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
			SetPictureBoxIcon(pictureBox, trayIcon);

			pictureBox.MouseDoubleClick += (s, e) =>
				{
					NativeMethods.SendNotifyMessage(trayIcon.hWnd, trayIcon.callbackMessage,
						(UIntPtr) trayIcon.id, NativeMethods.WM_LBUTTONDBLCLK);
					//NativeMethods.SetForegroundWindow(trayIcon.hWnd);
				};
			pictureBox.MouseDown += (s, e) =>
				{
					if (e.Button == MouseButtons.Left)
					{
						NativeMethods.SetForegroundWindow(trayIcon.hWnd);
						NativeMethods.SendNotifyMessage(trayIcon.hWnd, trayIcon.callbackMessage,
							(UIntPtr) trayIcon.id, NativeMethods.WM_LBUTTONDOWN);
					}
					else if (e.Button == MouseButtons.Right)
					{
						NativeMethods.SetForegroundWindow(trayIcon.hWnd);
						NativeMethods.SendNotifyMessage(trayIcon.hWnd, trayIcon.callbackMessage,
							(UIntPtr) trayIcon.id, NativeMethods.WM_RBUTTONDOWN);
					}
				};

			return pictureBox;
		}

		private static ToolTip CreateToolTip(PictureBox pictureBox, string tip)
		{
			var toolTip = new ToolTip();
			toolTip.ShowAlways = true;
			toolTip.SetToolTip(pictureBox, tip);

			return toolTip;
		}

		private static bool TrayIconVisible(SystemTray.TrayIcon trayIcon)
		{
			return (trayIcon.state & NativeMethods.IconState.NIS_HIDDEN) == 0;
		}

		private static void TrayIconAdded(NativeMethods.NOTIFYICONDATA iconData)
		{
			// TODO: GUID not taken into account
			var tuple = new Tuple<int, uint>(iconData.hWnd, iconData.uID);

			// add to visible or to hidden icons
			if (SetButtonsCount())
			{
				OnIconAdded(iconData, tuple);
			}
			else
			{
				OnHiddenIconAdded(iconData, tuple);
			}
		}

		private static void TrayIconModified(NativeMethods.NOTIFYICONDATA iconData)
		{
			SetButtonsCount();
			OnIconModified(iconData, new Tuple<int, uint>(iconData.hWnd, iconData.uID));
		}

		private static void TrayIconDeleted(NativeMethods.NOTIFYICONDATA iconData)
		{
			SetButtonsCount();
			OnIconRemoved(new Tuple<int, uint>(iconData.hWnd, iconData.uID));
		}

		private static bool SetButtonsCount()
		{
			var newButtonsCount = SystemTray.GetButtonsCount(SystemTray.trayHandle);
			if (newButtonsCount != -1 && newButtonsCount != nonHiddenButtonsCount)
			{
				nonHiddenButtonsCount = newButtonsCount;
				return true;
			}
			return false;
		}

		private static bool UpdateIconData(Tuple<SystemTray.TrayIcon, PictureBox, ToolTip> prevIconData, NativeMethods.NOTIFYICONDATA iconData)
		{
			PictureBox pictureBox = prevIconData.Item2;
			var trayIcon = prevIconData.Item1;

			// updates the message callback
			if ((iconData.uFlags & NativeMethods.IconDataMembers.NIF_MESSAGE) != 0 &&
				iconData.uCallbackMessage != trayIcon.callbackMessage)
			{
				trayIcon.callbackMessage = iconData.uCallbackMessage;
			}

			// updates the message tip
			if ((iconData.uFlags & NativeMethods.IconDataMembers.NIF_TIP) != 0 && iconData.szTip != trayIcon.tooltip)
			{
				trayIcon.tooltip = iconData.szTip;
				prevIconData.Item3.SetToolTip(pictureBox, trayIcon.tooltip);
			}

			// updates the icon
			if ((iconData.uFlags & NativeMethods.IconDataMembers.NIF_ICON) != 0 &&
				(IntPtr) iconData.hIcon != trayIcon.iconHandle && iconData.hIcon != 0)
			{
				trayIcon.iconHandle = (IntPtr) iconData.hIcon;
				SetPictureBoxIcon(pictureBox, trayIcon);
			}

			// updates the state
			if ((iconData.uFlags & NativeMethods.IconDataMembers.NIF_STATE) != 0 &&
				(iconData.dwState & iconData.dwStateMask) != trayIcon.state)
			{
				trayIcon.state = iconData.dwState & iconData.dwStateMask;
				return true;
			}

			return false;
		}

		private static void SetPictureBoxIcon(PictureBox pictureBox, SystemTray.TrayIcon trayIcon)
		{
			try
			{
				var bitmap = Bitmap.FromHicon(trayIcon.iconHandle);
				pictureBox.Image = new Bitmap(bitmap, Windawesome.smallIconSize);
			}
			catch
			{
				trayIcon.iconHandle = Properties.Resources.Icon_missing.GetHicon();
				pictureBox.Image = new Bitmap(Properties.Resources.Icon_missing, Windawesome.smallIconSize);
			}
		}

		private IEnumerable<PictureBox> GetPictureBoxes()
		{
			return icons.Where(t => (t.Value.Item1.state & NativeMethods.IconState.NIS_HIDDEN) == 0).Select(t => t.Value.Item2);
		}

		#region IWidget Members

		public WidgetType GetWidgetType()
		{
			return WidgetType.FixedWidth;
		}

		public void StaticInitializeWidget(Windawesome windawesome, Config config)
		{
			// cleans the resources taken by the system tray manager
			SystemTray.Dispose();

			if (Windawesome.isRunningElevated)
			{
				if (Windawesome.isAtLeast7)
				{
					NativeMethods.ChangeWindowMessageFilterEx(Windawesome.handle, NativeMethods.WM_COPYDATA, NativeMethods.MSGFLT_ADD, IntPtr.Zero);
				}
				else if (Windawesome.isAtLeastVista)
				{
					NativeMethods.ChangeWindowMessageFilter(NativeMethods.WM_COPYDATA, NativeMethods.MSGFLT_ADD);
				}
			}

			// system tray hook
			if (NativeMethods.RegisterSystemTrayHook(Windawesome.handle))
			{
				Windawesome.RegisterMessage(NativeMethods.WM_COPYDATA, OnSystemTrayMessage);
			}
		}

		public void InitializeWidget(Bar bar)
		{
			this.bar = bar;
		}

		public IEnumerable<Control> GetControls(int left, int right)
		{
			isLeft = right == -1;

			this.RepositionControls(left, right);

			return GetPictureBoxes();
		}

		public void RepositionControls(int left, int right)
		{
			this.left = left;
			this.right = right;

			var trayIcons = GetPictureBoxes();
			if (isLeft)
			{
				foreach (var pictureBox in trayIcons)
				{
					pictureBox.Size = new Size(bar.barHeight, bar.barHeight);
					pictureBox.Location = new Point(left, 0);
					left += bar.barHeight;
				}
				this.right = left;
			}
			else
			{
				foreach (var pictureBox in trayIcons.Reverse())
				{
					pictureBox.Size = new Size(bar.barHeight, bar.barHeight);
					right -= bar.barHeight;
					pictureBox.Location = new Point(right, 0);
				}
				this.left = right;
			}
		}

		public int GetLeft()
		{
			return left;
		}

		public int GetRight()
		{
			return right;
		}

		public void WidgetShown()
		{
		}

		public void WidgetHidden()
		{
		}

		public void StaticDispose()
		{
			// unregister system tray hook
			NativeMethods.UnregisterSystemTrayHook();
		}

		public void Dispose()
		{
		}

		#endregion

		private class SystemTray
		{
			private const uint TB_GETBUTTON = 0x417;
			private const uint TB_BUTTONCOUNT = 0x418;

			internal static readonly IntPtr trayHandle;
			internal static readonly IntPtr hiddenTrayHandle;

			private static readonly int TBBUTTONsize;
			private static readonly IntPtr explorerProcessHandle;
			private static readonly IntPtr buttonMemory;
			private static StringBuilder sb;

			static SystemTray()
			{
				trayHandle = FindTrayHandle();
				if (Windawesome.isAtLeast7)
				{
					hiddenTrayHandle = FindHiddenTrayHandle();
				}

				if (Environment.Is64BitOperatingSystem)
				{
					TBBUTTONsize = Marshal.SizeOf(typeof(NativeMethods.TBBUTTON64.ButtonData));
				}
				else
				{
					TBBUTTONsize = Marshal.SizeOf(typeof(NativeMethods.TBBUTTON32.ButtonData));
				}
				sb = new StringBuilder(1024);

				int explorerPID;
				NativeMethods.GetWindowThreadProcessId(trayHandle, out explorerPID);

				explorerProcessHandle = NativeMethods.OpenProcess(NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ, false, explorerPID);
				if (explorerProcessHandle == IntPtr.Zero)
				{
					throw new Exception("Could not open explorer.exe process with PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION permissions");
				}

				buttonMemory = NativeMethods.VirtualAllocEx(explorerProcessHandle, IntPtr.Zero, TBBUTTONsize, NativeMethods.MEM_COMMIT, NativeMethods.PAGE_READWRITE);
				if (buttonMemory == IntPtr.Zero)
				{
					NativeMethods.CloseHandle(explorerProcessHandle);
					throw new Exception("Could not VirtualAllocEx some memory in explorer.exe");
				}
			}

			internal static void Dispose()
			{
				if (explorerProcessHandle != IntPtr.Zero)
				{
					if (buttonMemory != IntPtr.Zero)
					{
						NativeMethods.VirtualFreeEx(explorerProcessHandle, buttonMemory, 0, NativeMethods.MEM_RELEASE);
					}
					NativeMethods.CloseHandle(explorerProcessHandle);
				}
				sb = null;
			}

			private static IntPtr FindTrayHandle()
			{
				IntPtr hWnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
				hWnd = NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "TrayNotifyWnd", null);
				hWnd = NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "SysPager", null);
				return NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "ToolbarWindow32", null);
			}

			private static IntPtr FindHiddenTrayHandle()
			{
				IntPtr hWnd = NativeMethods.FindWindow("NotifyIconOverflowWindow", null);
				return NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "ToolbarWindow32", null);
			}

			private class ITrayIconData
			{
				public NativeMethods.ITBBUTTON Button;
				public NativeMethods.ITRAYDATA TrayData;
			}

			private class TrayIconData32 : ITrayIconData
			{
				public TrayIconData32()
				{
					Button = new NativeMethods.TBBUTTON32();
					TrayData = new NativeMethods.TRAYDATA32();
				}
			}

			private class TrayIconData64 : ITrayIconData
			{
				public TrayIconData64()
				{
					Button = new NativeMethods.TBBUTTON64();
					TrayData = new NativeMethods.TRAYDATA64();
				}
			}

			internal class TrayIcon
			{
				public IntPtr hWnd;
				public uint callbackMessage;
				public uint id;
				public IntPtr iconHandle;
				public string tooltip;
				public NativeMethods.IconState state;

				public TrayIcon()
				{
					tooltip = "";
				}
			}

			internal static int GetButtonsCount(IntPtr trayHandle)
			{
				IntPtr buttonsCountIntPtr;

				if (NativeMethods.SendMessageTimeout(
					trayHandle,
					TB_BUTTONCOUNT,
					UIntPtr.Zero,
					IntPtr.Zero,
					NativeMethods.SMTO.SMTO_BLOCK | NativeMethods.SMTO.SMTO_NOTIMEOUTIFNOTHUNG | NativeMethods.SMTO.SMTO_ABORTIFHUNG,
					1000, out buttonsCountIntPtr) == IntPtr.Zero)
				{
					return -1;
				}

				return buttonsCountIntPtr.ToInt32();
			}

			internal static IEnumerable<TrayIcon> GetButtons(IntPtr trayHandle)
			{
				if (Environment.Is64BitOperatingSystem)
				{
					return GetButtons<TrayIconData64>(trayHandle);
				}
				else
				{
					return GetButtons<TrayIconData32>(trayHandle);
				}
			}

			private static IEnumerable<TrayIcon> GetButtons<TrayIconData>(IntPtr trayHandle)
				where TrayIconData : ITrayIconData, new()
			{
				int buttonsCount = GetButtonsCount(trayHandle);
				TrayIconData data = new TrayIconData();
				var result = new LinkedList<TrayIcon>();
				TrayIcon trayIcon;

				for (int i = 0; i < buttonsCount; i++)
				{
					NativeMethods.SendMessageTimeout(
						trayHandle,
						TB_GETBUTTON,
						(UIntPtr) i,
						buttonMemory,
						NativeMethods.SMTO.SMTO_BLOCK | NativeMethods.SMTO.SMTO_NOTIMEOUTIFNOTHUNG | NativeMethods.SMTO.SMTO_ABORTIFHUNG,
						1000, IntPtr.Zero);

					uint numberOfBytesRead;

					if (!data.Button.Initialize(explorerProcessHandle, buttonMemory, out numberOfBytesRead))
					{
						continue;
					}

					if (!data.TrayData.Initialize(explorerProcessHandle, data.Button.dwData, out numberOfBytesRead))
					{
						continue;
					}

					trayIcon = new TrayIcon();
					if (NativeMethods.ReadProcessMemory(explorerProcessHandle, data.Button.iString, sb, sb.Capacity, out numberOfBytesRead))
					{
						trayIcon.tooltip = sb.ToString();
					}

					trayIcon.callbackMessage = data.TrayData.uCallbackMessage;
					trayIcon.id = data.TrayData.uID;
					trayIcon.hWnd = data.TrayData.hWnd;
					trayIcon.iconHandle = data.TrayData.hIcon;
					trayIcon.state = 0;
					if ((data.Button.fsState & NativeMethods.TBSTATE_HIDDEN) == NativeMethods.TBSTATE_HIDDEN)
					{
						trayIcon.state |= NativeMethods.IconState.NIS_HIDDEN;
					}

					result.AddLast(trayIcon);
				}

				return result;
			}
		}
	}
}
