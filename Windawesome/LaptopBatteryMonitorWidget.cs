using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Windawesome
{
	public sealed class LaptopBatteryMonitorWidget : IFixedWidthWidget
	{
		private Bar bar;

		private Label label;
		private bool isLeft;
		private readonly Timer updateTimer;
		private readonly string textForCharging;
		private readonly string textForNotCharging;
		private readonly string prefix;
		private readonly string postfix;
		private readonly Color backgroundColor;
		private readonly Color foregroundColor;

		public LaptopBatteryMonitorWidget(string textForCharging = "C", string textForNotCharging = "B",
			string prefix = " ", string postfix = "%", int updateTime = 60000,
			Color? backgroundColor = null, Color? foregroundColor = null)
		{
			updateTimer = new Timer { Interval = updateTime };
			updateTimer.Tick += OnTimerTick;

			this.textForCharging = textForCharging;
			this.textForNotCharging = textForNotCharging;

			this.prefix = prefix;
			this.postfix = postfix;

			this.backgroundColor = backgroundColor ?? Color.White;
			this.foregroundColor = foregroundColor ?? Color.Black;
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

			var oldLeft = label.Left;
			var oldRight = label.Right;
			var oldWidth = label.Width;
			label.Text = (powerStatus.PowerLineStatus == PowerLineStatus.Offline ?
				textForNotCharging : textForCharging) + prefix + (powerStatus.BatteryLifePercent * 100) + postfix;

			label.Width = TextRenderer.MeasureText(label.Text, label.Font).Width;
			if (oldWidth != label.Width)
			{
				this.RepositionControls(oldLeft, oldRight);
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
				textForNotCharging : textForCharging) + prefix + (powerStatus.BatteryLifePercent * 100) + postfix, 0);
			label.BackColor = backgroundColor;
			label.ForeColor = foregroundColor;
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
					label.Text = @"NO BAT";
					return ;
			}

			bar.BarShown += () => updateTimer.Start();
			bar.BarHidden += () => updateTimer.Stop();
		}

		IEnumerable<Control> IFixedWidthWidget.GetInitialControls(bool isLeft)
		{
			this.isLeft = isLeft;

			return new[] { label };
		}

		public void RepositionControls(int left, int right)
		{
			this.label.Location = this.isLeft ? new Point(left, 0) : new Point(right - this.label.Width, 0);
		}

		int IWidget.GetLeft()
		{
			return label.Left;
		}

		int IWidget.GetRight()
		{
			return label.Right;
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
