using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Windawesome
{
	public class LayoutWidget : IFixedWidthWidget
	{
		private Label layoutLabel;
		private readonly Color backgroundColor;
		private readonly Color foregroundColor;
		private readonly Action onClick;
		private Bar bar;
		private bool isLeft;

		public LayoutWidget(Color? backColor = null, Color? foreColor = null, Action onClick = null)
		{
			backgroundColor = backColor ?? Color.FromArgb(0x99, 0xB4, 0xD1);
			foregroundColor = foreColor ?? Color.FromArgb(0x00, 0x00, 0x00);

			this.onClick = onClick;
		}

		private void OnWorkspaceLayoutChanged(Workspace workspace)
		{
			if (workspace.Monitor == bar.Monitor && workspace.IsWorkspaceVisible)
			{
				var oldLeft = layoutLabel.Left;
				var oldRight = layoutLabel.Right;
				var oldWidth = layoutLabel.Width;
				layoutLabel.Text = workspace.Layout.LayoutSymbol();
				layoutLabel.Width = TextRenderer.MeasureText(layoutLabel.Text, layoutLabel.Font).Width;
				if (layoutLabel.Width != oldWidth)
				{
					this.RepositionControls(oldLeft, oldRight);
					bar.DoFixedWidthWidgetWidthChanged(this);
				}
			}
		}

		#region IWidget Members

		void IWidget.StaticInitializeWidget(Windawesome windawesome)
		{
		}

		void IWidget.InitializeWidget(Bar bar)
		{
			this.bar = bar;

			bar.BarShown += () => OnWorkspaceLayoutChanged(bar.Monitor.CurrentVisibleWorkspace);

			Workspace.LayoutUpdated += () => OnWorkspaceLayoutChanged(bar.Monitor.CurrentVisibleWorkspace);
			Workspace.WorkspaceShown += OnWorkspaceLayoutChanged;
			Workspace.WorkspaceLayoutChanged += (ws, _) => OnWorkspaceLayoutChanged(ws);

			layoutLabel = bar.CreateLabel("", 0);
			layoutLabel.TextAlign = ContentAlignment.MiddleCenter;
			layoutLabel.BackColor = backgroundColor;
			layoutLabel.ForeColor = foregroundColor;
			if (onClick != null)
			{
				layoutLabel.Click += (unused1, unused2) => onClick();
			}
		}

		IEnumerable<Control> IFixedWidthWidget.GetInitialControls(bool isLeft)
		{
			this.isLeft = isLeft;

			return new Control[] { layoutLabel };
		}

		public void RepositionControls(int left, int right)
		{
			this.layoutLabel.Location = this.isLeft ? new Point(left, 0) : new Point(right - this.layoutLabel.Width, 0);
		}

		int IWidget.GetLeft()
		{
			return layoutLabel.Left;
		}

		int IWidget.GetRight()
		{
			return layoutLabel.Right;
		}

		void IWidget.StaticDispose()
		{
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
