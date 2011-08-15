using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Windawesome
{
	public class SystemTrayWidget : IFixedWidthWidget
	{
		private readonly Dictionary<Tuple<int, uint>, Tuple<SystemTray.TrayIcon, PictureBox, ToolTip>> icons; // (hWnd, uID) -> (TrayIcon, PictureBox, ToolTip)

		private Bar bar;
		private int left, right;
		private bool isLeft;
		private readonly bool showFullSystemTray;

		#region Events

		private delegate void IconAddedEventHandler(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple);
		private static event IconAddedEventHandler IconAdded;

		private delegate void IconModifiedEventHandler(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple);
		private static event IconModifiedEventHandler IconModified;

		private delegate void IconRemovedEventHandler(Tuple<int, uint> tuple);
		private static event IconRemovedEventHandler IconRemoved;

		private delegate void HiddenIconAddedEventHandler(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple);
		private static event HiddenIconAddedEventHandler HiddenIconAdded;

		private static void DoHiddenIconAdded(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple)
		{
			if (HiddenIconAdded != null)
			{
				HiddenIconAdded(iconData, tuple);
			}
		}

		#endregion

		public SystemTrayWidget(bool showFullSystemTray = false)
		{
			this.showFullSystemTray = Windawesome.isAtLeast7 && showFullSystemTray;

			icons = new Dictionary<Tuple<int, uint>, Tuple<SystemTray.TrayIcon, PictureBox, ToolTip>>(10);

			IconAdded	 += OnIconAdded;
			IconModified += OnIconModified;
			IconRemoved	 += OnIconRemoved;

			GetIcons();
		}

		private void GetIcons()
		{
			foreach (var icon in SystemTray.GetButtons(SystemTray.trayHandle))
			{
				var pictureBox = CreatePictureBox(icon);
				var toolTip = CreateToolTip(pictureBox, icon.tooltip);
				icons[new Tuple<int, uint>((int) icon.hWnd, icon.id)] = new Tuple<SystemTray.TrayIcon, PictureBox, ToolTip>(icon, pictureBox, toolTip);
			}

			if (showFullSystemTray)
			{
				HiddenIconAdded += OnIconAdded;

				foreach (var icon in SystemTray.GetButtons(SystemTray.hiddenTrayHandle))
				{
					var pictureBox = CreatePictureBox(icon);
					var toolTip = CreateToolTip(pictureBox, icon.tooltip);
					icons[new Tuple<int, uint>((int) icon.hWnd, icon.id)] = new Tuple<SystemTray.TrayIcon, PictureBox, ToolTip>(icon, pictureBox, toolTip);
				}
			}
		}

		private void OnIconAdded(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple)
		{
			if (!icons.ContainsKey(tuple))
			{
				var trayIcon = new SystemTray.TrayIcon { hWnd = (IntPtr) iconData.hWnd, id = iconData.uID };
				var pictureBox = CreatePictureBox(trayIcon);
				var t = new Tuple<SystemTray.TrayIcon, PictureBox, ToolTip>(trayIcon, pictureBox, CreateToolTip(pictureBox, trayIcon.tooltip));
				UpdateIconData(t, iconData);
				icons[tuple] = t;
				if (TrayIconVisible(trayIcon))
				{
					RepositionControls(left, right);
					bar.DoWidgetControlsChanged(this, new PictureBox[0], new[] { pictureBox });
				}
			}
		}

		private void OnIconModified(NativeMethods.NOTIFYICONDATA iconData, Tuple<int, uint> tuple)
		{
			Tuple<SystemTray.TrayIcon, PictureBox, ToolTip> trayIcon;

			if (icons.TryGetValue(tuple, out trayIcon) && UpdateIconData(trayIcon, iconData))
			{
				RepositionControls(left, right);
				if (TrayIconVisible(trayIcon.Item1))
				{
					bar.DoWidgetControlsChanged(this, new PictureBox[0], new[] { trayIcon.Item2 });
				}
				else
				{
					bar.DoWidgetControlsChanged(this, new[] { trayIcon.Item2 }, new PictureBox[0]);
				}
			}
		}

		private void OnIconRemoved(Tuple<int, uint> tuple)
		{
			Tuple<SystemTray.TrayIcon, PictureBox, ToolTip> trayIcon;

			if (icons.TryGetValue(tuple, out trayIcon))
			{
				icons.Remove(tuple);

				RepositionControls(left, right);
				bar.DoWidgetControlsChanged(this, new[] { trayIcon.Item2 }, new PictureBox[0]);
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
					if ((trayData.nid.uFlags.HasFlag(NativeMethods.IconDataMembers.NIF_ICON)) && trayData.dwMessage != NativeMethods.NIM.NIM_DELETE)
					{
						trayData.nid.hIcon = NativeMethods.CopyIcon((IntPtr) trayData.nid.hIcon).ToInt32();
					}

					NativeMethods.ReplyMessage(NativeMethods.IntPtrOne);

					switch (trayData.dwMessage)
					{
						case NativeMethods.NIM.NIM_ADD:
							TrayIconAdded(trayData.nid);
							break;
						case NativeMethods.NIM.NIM_MODIFY:
							TrayIconModified(trayData.nid);
							break;
						case NativeMethods.NIM.NIM_DELETE:
							TrayIconDeleted(trayData.nid);
							break;
					}

					if ((trayData.nid.uFlags.HasFlag(NativeMethods.IconDataMembers.NIF_ICON)) && trayData.dwMessage != NativeMethods.NIM.NIM_DELETE)
					{
						NativeMethods.DestroyIcon((IntPtr) trayData.nid.hIcon);
					}

					m.Result = NativeMethods.IntPtrOne; // return TRUE
					return true;
				}
			}

			return false;
		}

		private static void OnMouseEvent(SystemTray.TrayIcon trayIcon, MouseEventArgs e, IntPtr leftButtonAction, IntPtr rightButtonAction)
		{
			switch (e.Button)
			{
				case MouseButtons.Left:
					NativeMethods.SendNotifyMessage(trayIcon.hWnd, trayIcon.callbackMessage,
						(UIntPtr) trayIcon.id, leftButtonAction);
					break;
				case MouseButtons.Right:
					NativeMethods.SendNotifyMessage(trayIcon.hWnd, trayIcon.callbackMessage,
						(UIntPtr) trayIcon.id, rightButtonAction);
					break;
			}
		}

		private static PictureBox CreatePictureBox(SystemTray.TrayIcon trayIcon)
		{
			var pictureBox = new PictureBox { SizeMode = PictureBoxSizeMode.CenterImage };
			SetPictureBoxIcon(pictureBox, trayIcon);

			pictureBox.MouseDoubleClick += (_, e) => OnMouseEvent(trayIcon, e, NativeMethods.WM_LBUTTONDBLCLK, NativeMethods.WM_RBUTTONDBLCLK);
			pictureBox.MouseDown += (_, e) => OnMouseEvent(trayIcon, e, NativeMethods.WM_LBUTTONDOWN, NativeMethods.WM_RBUTTONDOWN);
			pictureBox.MouseUp += (_, e) => OnMouseEvent(trayIcon, e, NativeMethods.WM_LBUTTONUP, NativeMethods.WM_RBUTTONUP);

			return pictureBox;
		}

		private static ToolTip CreateToolTip(PictureBox pictureBox, string tip)
		{
			var toolTip = new ToolTip { ShowAlways = true };
			toolTip.SetToolTip(pictureBox, tip);

			return toolTip;
		}

		private static bool TrayIconVisible(SystemTray.TrayIcon trayIcon)
		{
			return !trayIcon.state.HasFlag(NativeMethods.IconState.NIS_HIDDEN);
		}

		private static void TrayIconAdded(NativeMethods.NOTIFYICONDATA iconData)
		{
			// TODO: GUID not taken into account
			var tuple = new Tuple<int, uint>(iconData.hWnd, iconData.uID);

			// add to visible or to hidden icons
			if (!Windawesome.isAtLeast7 || SystemTray.ContainsButton(SystemTray.trayHandle, (IntPtr) iconData.hWnd, iconData.uID))
			{
				IconAdded(iconData, tuple);
			}
			else
			{
				DoHiddenIconAdded(iconData, tuple);
			}
		}

		private static void TrayIconModified(NativeMethods.NOTIFYICONDATA iconData)
		{
			IconModified(iconData, new Tuple<int, uint>(iconData.hWnd, iconData.uID));
		}

		private static void TrayIconDeleted(NativeMethods.NOTIFYICONDATA iconData)
		{
			IconRemoved(new Tuple<int, uint>(iconData.hWnd, iconData.uID));
		}

		private static bool UpdateIconData(Tuple<SystemTray.TrayIcon, PictureBox, ToolTip> prevIconData, NativeMethods.NOTIFYICONDATA iconData)
		{
			var pictureBox = prevIconData.Item2;
			var trayIcon = prevIconData.Item1;

			// updates the message callback
			if ((iconData.uFlags.HasFlag(NativeMethods.IconDataMembers.NIF_MESSAGE)) &&
				iconData.uCallbackMessage != trayIcon.callbackMessage)
			{
				trayIcon.callbackMessage = iconData.uCallbackMessage;
			}

			// updates the message tip
			if ((iconData.uFlags.HasFlag(NativeMethods.IconDataMembers.NIF_TIP)) && iconData.szTip != trayIcon.tooltip)
			{
				trayIcon.tooltip = iconData.szTip;
				prevIconData.Item3.SetToolTip(pictureBox, trayIcon.tooltip);
			}

			// updates the icon
			if ((iconData.uFlags.HasFlag(NativeMethods.IconDataMembers.NIF_ICON)) &&
				(IntPtr) iconData.hIcon != trayIcon.iconHandle && iconData.hIcon != 0)
			{
				trayIcon.iconHandle = (IntPtr) iconData.hIcon;
				SetPictureBoxIcon(pictureBox, trayIcon);
			}

			// updates the state
			if ((iconData.uFlags.HasFlag(NativeMethods.IconDataMembers.NIF_STATE)) &&
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
			return icons.Values.Where(t => TrayIconVisible(t.Item1)).Select(t => t.Item2);
		}

		#region IWidget Members

		void IWidget.StaticInitializeWidget(Windawesome windawesome, Config config)
		{
			if (Windawesome.isRunningElevated)
			{
				if (Windawesome.isAtLeast7)
				{
					NativeMethods.ChangeWindowMessageFilterEx(windawesome.Handle, NativeMethods.WM_COPYDATA, NativeMethods.MSGFLT_ADD, IntPtr.Zero);
				}
				else
				{
					NativeMethods.ChangeWindowMessageFilter(NativeMethods.WM_COPYDATA, NativeMethods.MSGFLT_ADD);
				}
			}

			// system tray hook
			if (NativeMethods.RegisterSystemTrayHook(windawesome.Handle))
			{
				Windawesome.RegisterMessage(NativeMethods.WM_COPYDATA, OnSystemTrayMessage);
			}
		}

		void IWidget.InitializeWidget(Bar bar)
		{
			this.bar = bar;
		}

		IEnumerable<Control> IWidget.GetControls(int left, int right)
		{
			isLeft = right == -1;

			this.RepositionControls(left, right);

			return GetPictureBoxes();
		}

		public void RepositionControls(int left, int right)
		{
			var trayIcons = GetPictureBoxes();
			if (isLeft)
			{
				this.left = left;
				foreach (var pictureBox in trayIcons)
				{
					pictureBox.Size = new Size(bar.GetBarHeight(), bar.GetBarHeight());
					pictureBox.Location = new Point(left, 0);
					left += bar.GetBarHeight();
				}
				this.right = left;
			}
			else
			{
				this.right = right;
				foreach (var pictureBox in trayIcons.Reverse())
				{
					pictureBox.Size = new Size(bar.GetBarHeight(), bar.GetBarHeight());
					right -= bar.GetBarHeight();
					pictureBox.Location = new Point(right, 0);
				}
				this.left = right;
			}
		}

		int IWidget.GetLeft()
		{
			return left;
		}

		int IWidget.GetRight()
		{
			return right;
		}

		void IWidget.WidgetShown()
		{
		}

		void IWidget.WidgetHidden()
		{
		}

		void IWidget.StaticDispose()
		{
			// unregister system tray hook
			NativeMethods.UnregisterSystemTrayHook();

			// cleans the resources taken by the system tray manager
			SystemTray.Dispose();
		}

		void IWidget.Dispose()
		{
		}

		void IWidget.Refresh()
		{
			var oldIcons = GetPictureBoxes().ToArray();
			icons.Clear();
			GetIcons();
			RepositionControls(left, right);
			bar.DoWidgetControlsChanged(this, oldIcons, GetPictureBoxes());
		}

		#endregion

		private static class SystemTray
		{
			private const uint TB_GETBUTTON = 0x417;
			private const uint TB_BUTTONCOUNT = 0x418;

			internal static readonly IntPtr trayHandle;
			internal static readonly IntPtr hiddenTrayHandle;

			private static readonly int TBBUTTONSize;
			private static readonly IntPtr explorerProcessHandle;
			private static readonly IntPtr buttonMemory;
			private static readonly StringBuilder sb;

			static SystemTray()
			{
				trayHandle = FindTrayHandle();
				if (Windawesome.isAtLeast7)
				{
					hiddenTrayHandle = FindHiddenTrayHandle();
				}

				TBBUTTONSize = Marshal.SizeOf(
					Environment.Is64BitOperatingSystem ?
						typeof(NativeMethods.TBBUTTON64.ButtonData) :
						typeof(NativeMethods.TBBUTTON32.ButtonData));
				sb = new StringBuilder(128);

				int explorerPId;
				NativeMethods.GetWindowThreadProcessId(trayHandle, out explorerPId);

				explorerProcessHandle = NativeMethods.OpenProcess(NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ, false, explorerPId);
				if (explorerProcessHandle == IntPtr.Zero)
				{
					throw new Exception("Could not open explorer.exe process with PROCESS_VM_OPERATION | PROCESS_VM_READ permissions");
				}

				buttonMemory = NativeMethods.VirtualAllocEx(explorerProcessHandle, IntPtr.Zero, TBBUTTONSize, NativeMethods.MEM_COMMIT, NativeMethods.PAGE_READWRITE);
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
			}

			private static IntPtr FindTrayHandle()
			{
				var hWnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
				hWnd = NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "TrayNotifyWnd", null);
				hWnd = NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "SysPager", null);
				return NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "ToolbarWindow32", null);
			}

			private static IntPtr FindHiddenTrayHandle()
			{
				var hWnd = NativeMethods.FindWindow("NotifyIconOverflowWindow", null);
				return NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "ToolbarWindow32", null);
			}

			private abstract class TrayIconData
			{
				public NativeMethods.ITBBUTTON button;
				public NativeMethods.ITRAYDATA trayData;
			}

			private class TrayIconData32 : TrayIconData
			{
				public TrayIconData32()
				{
					this.button = new NativeMethods.TBBUTTON32();
					this.trayData = new NativeMethods.TRAYDATA32();
				}
			}

			private class TrayIconData64 : TrayIconData
			{
				public TrayIconData64()
				{
					this.button = new NativeMethods.TBBUTTON64();
					this.trayData = new NativeMethods.TRAYDATA64();
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

			private static int GetButtonsCount(IntPtr trayHandle)
			{
				IntPtr buttonsCountIntPtr;

				NativeMethods.SendMessageTimeout(
					trayHandle,
					TB_BUTTONCOUNT,
					UIntPtr.Zero,
					IntPtr.Zero,
					NativeMethods.SMTO.SMTO_BLOCK | NativeMethods.SMTO.SMTO_NOTIMEOUTIFNOTHUNG | NativeMethods.SMTO.SMTO_ABORTIFHUNG,
					1000, out buttonsCountIntPtr);

				return buttonsCountIntPtr.ToInt32();
			}

			private static bool GetButtonData<TTrayIconData>(IntPtr trayHandle, TTrayIconData data, int i)
				where TTrayIconData : TrayIconData
			{
				NativeMethods.SendMessageTimeout(
					trayHandle,
					TB_GETBUTTON,
					(UIntPtr) i,
					buttonMemory,
					NativeMethods.SMTO.SMTO_BLOCK | NativeMethods.SMTO.SMTO_NOTIMEOUTIFNOTHUNG | NativeMethods.SMTO.SMTO_ABORTIFHUNG,
					1000, IntPtr.Zero);

				uint numberOfBytesRead;
				return data.button.Initialize(explorerProcessHandle, buttonMemory, out numberOfBytesRead) &&
					data.trayData.Initialize(explorerProcessHandle, data.button.dwData, out numberOfBytesRead);
			}

			internal static IEnumerable<TrayIcon> GetButtons(IntPtr trayHandle)
			{
				return Environment.Is64BitOperatingSystem ?
					GetButtons<TrayIconData64>(trayHandle) :
					GetButtons<TrayIconData32>(trayHandle);
			}

			private static IEnumerable<TrayIcon> GetButtons<TTrayIconData>(IntPtr trayHandle)
				where TTrayIconData : TrayIconData, new()
			{
				var buttonsCount = GetButtonsCount(trayHandle);
				var data = new TTrayIconData();
				var result = new LinkedList<TrayIcon>();

				TrayIcon trayIcon;
				for (var i = 0; i < buttonsCount; i++)
				{
					if (!GetButtonData(trayHandle, data, i))
					{
						continue;
					}

					uint numberOfBytesRead;

					trayIcon = new TrayIcon();
					if (NativeMethods.ReadProcessMemory(explorerProcessHandle, data.button.iString, sb, sb.Capacity, out numberOfBytesRead))
					{
						trayIcon.tooltip = sb.ToString();
					}

					trayIcon.callbackMessage = data.trayData.uCallbackMessage;
					trayIcon.id = data.trayData.uID;
					trayIcon.hWnd = data.trayData.hWnd;
					trayIcon.iconHandle = data.trayData.hIcon;
					trayIcon.state = 0;
					if (data.button.fsState.HasFlag(NativeMethods.TBSTATE.TBSTATE_HIDDEN))
					{
						trayIcon.state |= NativeMethods.IconState.NIS_HIDDEN;
					}

					result.AddLast(trayIcon);
				}

				return result;
			}

			internal static bool ContainsButton(IntPtr trayHandle, IntPtr hWnd, uint id)
			{
				return Environment.Is64BitOperatingSystem ?
					ContainsButton<TrayIconData64>(trayHandle, hWnd, id) :
					ContainsButton<TrayIconData32>(trayHandle, hWnd, id);
			}

			private static bool ContainsButton<TTrayIconData>(IntPtr trayHandle, IntPtr hWnd, uint id)
				where TTrayIconData : TrayIconData, new()
			{
				var buttonsCount = GetButtonsCount(trayHandle);
				var data = new TTrayIconData();

				for (var i = 0; i < buttonsCount; i++)
				{
					if (!GetButtonData(trayHandle, data, i))
					{
						continue;
					}

					if (data.trayData.hWnd == hWnd && data.trayData.uID == id)
					{
						return true;
					}
				}

				return false;
			}
		}
	}
}
