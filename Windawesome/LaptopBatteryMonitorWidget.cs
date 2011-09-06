using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Windawesome
{
	public class LaptopBatteryMonitorWidget : IFixedWidthWidget
	{
		private Bar bar;

		private Label label;
		private int left, right;
		private bool isLeft;
		private readonly Timer updateTimer;
		private readonly string textForCharging;
		private readonly string textForNotCharging;
		private readonly string prefix;
		private readonly string postfix;
		private readonly Color backgroundColor;

		public LaptopBatteryMonitorWidget(string textForCharging = "C", string textForNotCharging = "B",
			string prefix = " ", string postfix = "%", int updateTime = 60000, Color? backgroundColor = null)
		{
			updateTimer = new Timer { Interval = updateTime };
			updateTimer.Tick += OnTimerTick;

			this.textForCharging = textForCharging;
			this.textForNotCharging = textForNotCharging;

			this.prefix = prefix;
			this.postfix = postfix;

			this.backgroundColor = backgroundColor ?? Color.White;
		}

		private void OnTimerTick(object sender, System.EventArgs e)
		{
			var powerStatus = SystemInformation.PowerStatus;
			switch (powerStatus.BatteryChargeStatus)
			{
				case BatteryChargeStatus.Critical:
					label.ForeColor = Color.Red;
					break;
				case BatteryChargeStatus.High:
					label.ForeColor = Color.Green;
					break;
				case BatteryChargeStatus.Low:
					label.ForeColor = Color.Orange;
					break;
			}

			var oldWidth = label.Width;
			label.Text = (powerStatus.PowerLineStatus == PowerLineStatus.Offline ?
				textForNotCharging : textForCharging) + prefix + (powerStatus.BatteryLifePercent * 100).ToString() + postfix;

			label.Width = TextRenderer.MeasureText(label.Text, label.Font).Width;
			if (oldWidth != label.Width)
			{
				this.RepositionControls(left, right);
				bar.DoFixedWidthWidgetWidthChanged(this);
			}
		}

		void IWidget.StaticInitializeWidget(Windawesome windawesome)
		{
		}

		void IWidget.InitializeWidget(Bar bar)
		{
			this.bar = bar;

			var powerStatus = SystemInformation.PowerStatus;
			label = bar.CreateLabel((powerStatus.PowerLineStatus == PowerLineStatus.Offline ?
				textForNotCharging : textForCharging) + prefix + (powerStatus.BatteryLifePercent * 100).ToString() + postfix, 0);
			label.BackColor = backgroundColor;
			label.TextAlign = ContentAlignment.MiddleCenter;
			switch (powerStatus.BatteryChargeStatus)
			{
				case BatteryChargeStatus.Critical:
					label.ForeColor = Color.Red;
					break;
				case BatteryChargeStatus.High:
					label.ForeColor = Color.Green;
					break;
				case BatteryChargeStatus.Low:
					label.ForeColor = Color.Orange;
					break;
				case BatteryChargeStatus.NoSystemBattery:
					label.ForeColor = Color.Black;
					label.Text = "NO BAT";
					return ;
			}

			bar.BarShown += () => updateTimer.Start();
			bar.BarHidden += () => updateTimer.Stop();
		}

		IEnumerable<System.Windows.Forms.Control> IWidget.GetControls(int left, int right)
		{
			isLeft = right == -1;

			this.RepositionControls(left, right);

			return new[] { label };
		}

		public void RepositionControls(int left, int right)
		{
			if (isLeft)
			{
				this.left = left;
				label.Location = new Point(left, 0);
				this.right = label.Right;
			}
			else
			{
				this.right = right;
				label.Location = new Point(right - label.Width, 0);
				this.left = label.Left;
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

		void IWidget.StaticDispose()
		{
		}

		void IWidget.Dispose()
		{
		}

		void IWidget.Refresh()
		{
		}
	}
}
