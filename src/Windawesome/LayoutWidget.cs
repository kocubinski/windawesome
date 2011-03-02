using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Windawesome
{
	public class LayoutWidget : IWidget
	{
		private static Windawesome windawesome;
		private static Config config;

		private Label layoutLabel;
		private readonly Color backgroundColor;
		private readonly Color foregroundColor;
		private Bar bar;
		private int left, right;
		private bool isLeft;

		public LayoutWidget(Color? backColor = null, Color? foreColor = null)
		{
			backgroundColor = backColor ?? Color.FromArgb(0x99, 0xb4, 0xd1);
			foregroundColor = foreColor ?? Color.FromArgb(0x00, 0x00, 0x00);

			Windawesome.LayoutUpdated += UpdateLayoutLabel;
		}

		private void UpdateLayoutLabel()
		{
			int oldWidth = layoutLabel.Width;
			layoutLabel.Text = windawesome.CurrentWorkspace.LayoutSymbol;
			layoutLabel.Width = TextRenderer.MeasureText(layoutLabel.Text, layoutLabel.Font).Width;
			if (layoutLabel.Width != oldWidth)
			{
				this.RepositionControls(left, right);
				bar.OnFixedWidthWidgetWidthChanged(this);
			}
		}

		private void layoutLabel_Click(object sender, System.EventArgs e)
		{
			windawesome.CurrentWorkspace.ChangeLayout(
				config.layouts[(Array.IndexOf(config.layouts, windawesome.CurrentWorkspace.layout) + 1) % config.layouts.Length]);
		}

		#region IWidget Members

		public WidgetType GetWidgetType()
		{
			return WidgetType.FixedWidth;
		}

		public void StaticInitializeWidget(Windawesome windawesome, Config config)
		{
			LayoutWidget.windawesome = windawesome;
			LayoutWidget.config = config;
		}

		public void InitializeWidget(Bar bar)
		{
			this.bar = bar;

			layoutLabel = bar.CreateLabel("", 0);
			layoutLabel.TextAlign = ContentAlignment.MiddleCenter;
			layoutLabel.BackColor = backgroundColor;
			layoutLabel.ForeColor = foregroundColor;
			layoutLabel.Click += layoutLabel_Click;
		}

		public IEnumerable<Control> GetControls(int left, int right)
		{
			isLeft = right == -1;

			RepositionControls(left, right);

			return new Control[] { layoutLabel };
		}

		public void RepositionControls(int left, int right)
		{
			this.left = left;
			this.right = right;

			if (isLeft)
			{
				layoutLabel.Location = new Point(left, 0);
				this.right = layoutLabel.Right;
			}
			else
			{
				layoutLabel.Location = new Point(right - layoutLabel.Width, 0);
				this.left = layoutLabel.Left;
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
		}

		public void Dispose()
		{
		}

		#endregion
	}
}
