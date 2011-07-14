using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Windawesome
{
	public class LayoutWidget : IFixedWidthWidget
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
			backgroundColor = backColor ?? Color.FromArgb(0x99, 0xB4, 0xD1);
			foregroundColor = foreColor ?? Color.FromArgb(0x00, 0x00, 0x00);

			Windawesome.LayoutUpdated += OnUpdateLayoutLabel;
		}

		private void OnUpdateLayoutLabel()
		{
			var oldWidth = layoutLabel.Width;
			layoutLabel.Text = windawesome.CurrentWorkspace.LayoutSymbol;
			layoutLabel.Width = TextRenderer.MeasureText(layoutLabel.Text, layoutLabel.Font).Width;
			if (layoutLabel.Width != oldWidth)
			{
				this.RepositionControls(left, right);
				bar.DoFixedWidthWidgetWidthChanged(this);
			}
		}

		private static void LayoutLabelClick(object sender, EventArgs e)
		{
			windawesome.CurrentWorkspace.ChangeLayout(
				config.Layouts[(Array.IndexOf(config.Layouts, windawesome.CurrentWorkspace.Layout) + 1) % config.Layouts.Length]);
		}

		#region IWidget Members

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
			layoutLabel.Click += LayoutLabelClick;
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
