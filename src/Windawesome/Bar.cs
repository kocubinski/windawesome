using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public sealed class Bar : IBar
	{
		private static readonly HashSet<Type> widgetTypes = new HashSet<Type>();

		private readonly int barHeight;
		private readonly NonActivatableForm form;
		private readonly IFixedWidthWidget[] leftAlignedWidgets;
		private readonly IFixedWidthWidget[] rightAlignedWidgets;
		private readonly ISpanWidget[] middleAlignedWidgets;
		private readonly Font font;

		private int rightmostLeftAlign;
		private int leftmostRightAlign;

		#region Events

		private delegate void SpanWidgetControlsAddedEventHandler(ISpanWidget widget, IEnumerable<Control> controls);
		private event SpanWidgetControlsAddedEventHandler SpanWidgetControlsAdded;

		private delegate void SpanWidgetControlsRemovedEventHandler(ISpanWidget widget, IEnumerable<Control> controls);
		private event SpanWidgetControlsRemovedEventHandler SpanWidgetControlsRemoved;

		private delegate void FixedWidthWidgetWidthChangedEventHandler(IFixedWidthWidget widget);
		private event FixedWidthWidgetWidthChangedEventHandler FixedWidthWidgetWidthChanged;

		private delegate void WidgetControlsChangedEventHandler(IWidget widget, IEnumerable<Control> oldControls, IEnumerable<Control> newControls);
		private event WidgetControlsChangedEventHandler WidgetControlsChanged;

		public void DoWidgetControlsChanged(IWidget widget, IEnumerable<Control> controlsRemoved, IEnumerable<Control> controlsAdded)
		{
			WidgetControlsChanged(widget, controlsRemoved, controlsAdded);
		}

		public void DoSpanWidgetControlsAdded(ISpanWidget widget, IEnumerable<Control> controls)
		{
			SpanWidgetControlsAdded(widget, controls);
		}

		public void DoSpanWidgetControlsRemoved(ISpanWidget widget, IEnumerable<Control> controls)
		{
			SpanWidgetControlsRemoved(widget, controls);
		}

		public void DoFixedWidthWidgetWidthChanged(IFixedWidthWidget widget)
		{
			FixedWidthWidgetWidthChanged(widget);
		}

		#endregion

		private sealed class NonActivatableForm : Form
		{
			protected override CreateParams CreateParams
			{
				get
				{
					var createParams = base.CreateParams;
					// make the bar not activatable
					createParams.ExStyle |= (int) NativeMethods.WS_EX.WS_EX_NOACTIVATE;
					return createParams;
				}
			}
		}

		private NonActivatableForm CreateForm()
		{
			var newForm = new NonActivatableForm
				{
					StartPosition = FormStartPosition.Manual,
					FormBorderStyle = FormBorderStyle.FixedToolWindow,
					AutoValidate = AutoValidate.Disable,
					CausesValidation = false,
					ControlBox = false,
					MaximizeBox = false,
					MinimizeBox = false,
					ShowIcon = false,
					ShowInTaskbar = false,
					SizeGripStyle = SizeGripStyle.Hide,
					AutoScaleMode = AutoScaleMode.Font,
					AutoScroll = false,
					AutoSize = false,
					HelpButton = false,
					TopLevel = true,
					WindowState = FormWindowState.Normal,
					TopMost = true
				};

			newForm.VisibleChanged += this.OnFormVisibleChanged;
			newForm.FormClosing += (s, ea) => ea.Cancel = true;

			return newForm;
		}

		public override int GetHashCode()
		{
			return this.form.Handle.GetHashCode();
		}

		public override bool Equals(object obj)
		{
			var bar = obj as Bar;
			return bar != null && this.form.Handle == bar.form.Handle;
		}

		#region Construction and Destruction

		public Bar(IEnumerable<IFixedWidthWidget> leftAlignedWidgets, IEnumerable<IFixedWidthWidget> rightAlignedWidgets,
			IEnumerable<ISpanWidget> middleAlignedWidgets, int barHeight = 20, Font font = null, Color? backgroundColor = null)
		{
			this.leftAlignedWidgets = leftAlignedWidgets.ToArray();
			this.rightAlignedWidgets = rightAlignedWidgets.ToArray();
			this.middleAlignedWidgets = middleAlignedWidgets.ToArray();
			this.barHeight = barHeight;
			this.font = font ?? new Font("Lucida Console", 8);

			this.form = CreateForm();
			if (backgroundColor != null)
			{
				this.form.BackColor = backgroundColor.Value;
			}
		}

		#region IBar Members

		void IBar.InitializeBar(Windawesome windawesome, Config config)
		{
			// statically initialize all widgets
			// this statement uses the laziness of Where
			this.leftAlignedWidgets.Cast<IWidget>().Concat(this.rightAlignedWidgets).Concat(this.middleAlignedWidgets).
				Where(w => !widgetTypes.Contains(w.GetType())).
				ForEach(w => { w.StaticInitializeWidget(windawesome, config); widgetTypes.Add(w.GetType()); });

			WidgetControlsChanged = OnWidgetControlsChanged;
			SpanWidgetControlsAdded = OnSpanWidgetControlsAdded;
			SpanWidgetControlsRemoved = OnSpanWidgetControlsRemoved;
			FixedWidthWidgetWidthChanged = OnFixedWidthWidgetWidthChanged;

			leftAlignedWidgets.ForEach(w => w.InitializeWidget(this));
			rightAlignedWidgets.ForEach(w => w.InitializeWidget(this));
			middleAlignedWidgets.ForEach(w => w.InitializeWidget(this));

			PlaceControls();
		}

		void IBar.Dispose()
		{
			leftAlignedWidgets.ForEach(w => w.Dispose());
			rightAlignedWidgets.ForEach(w => w.Dispose());
			middleAlignedWidgets.ForEach(w => w.Dispose());

			// statically dispose of all widgets
			// this statement uses the laziness of Where
			this.leftAlignedWidgets.Cast<IWidget>().Concat(this.rightAlignedWidgets).Concat(this.middleAlignedWidgets).
				Where(w => widgetTypes.Contains(w.GetType())).
				ForEach(w => { w.StaticDispose(); widgetTypes.Remove(w.GetType()); });

			this.form.Dispose();
		}

		public int GetBarHeight()
		{
			return barHeight;
		}

		IntPtr IBar.Handle
		{
			get
			{
				return this.form.Handle;
			}
		}

		void IBar.OnSizeChanging(Size newSize)
		{
			if (this.form.Size != newSize)
			{
				ResizeWidgets(newSize);
			}
		}

		void IBar.Show()
		{
			this.form.Show();
		}

		void IBar.Hide()
		{
			this.form.Hide();
		}

		#endregion

		#endregion

		#region Event Handlers

		private void OnWidgetControlsChanged(IWidget widget, IEnumerable<Control> controlsRemoved, IEnumerable<Control> controlsAdded)
		{
			this.form.SuspendLayout();

			controlsRemoved.ForEach(this.form.Controls.Remove);
			controlsAdded.ForEach(this.form.Controls.Add);

			if (widget is IFixedWidthWidget)
			{
				ResizeWidgets(widget as IFixedWidthWidget);
			}

			this.form.ResumeLayout();
		}

		private void OnSpanWidgetControlsAdded(ISpanWidget widget, IEnumerable<Control> controls)
		{
			this.form.SuspendLayout();

			controls.ForEach(this.form.Controls.Add);

			this.form.ResumeLayout();
		}

		private void OnSpanWidgetControlsRemoved(ISpanWidget widget, IEnumerable<Control> controls)
		{
			this.form.SuspendLayout();

			controls.ForEach(this.form.Controls.Remove);

			this.form.ResumeLayout();
		}

		private void OnFixedWidthWidgetWidthChanged(IFixedWidthWidget widget)
		{
			this.form.SuspendLayout();

			ResizeWidgets(widget);

			this.form.ResumeLayout();
		}

		private void OnFormVisibleChanged(object sender, EventArgs e)
		{
			if (this.form.Visible)
			{
				leftAlignedWidgets.ForEach(w => w.WidgetShown());
				rightAlignedWidgets.ForEach(w => w.WidgetShown());
				middleAlignedWidgets.ForEach(w => w.WidgetShown());
			}
			else
			{
				leftAlignedWidgets.ForEach(w => w.WidgetHidden());
				rightAlignedWidgets.ForEach(w => w.WidgetHidden());
				middleAlignedWidgets.ForEach(w => w.WidgetHidden());
			}
		}

		#endregion

		private void ResizeWidgets(Size newSize)
		{
			RepositionLeftAlignedWidgets(0, 0);
			RepositionRightAlignedWidgets(rightAlignedWidgets.Length - 1, newSize.Width);
			RepositionMiddleAlignedWidgets();
		}

		private void ResizeWidgets(IFixedWidthWidget widget)
		{
			int index;
			if ((index = Array.IndexOf(leftAlignedWidgets, widget)) != -1)
			{
				RepositionLeftAlignedWidgets(index + 1, widget.GetRight());
			}
			else
			{
				RepositionRightAlignedWidgets(Array.IndexOf(rightAlignedWidgets, widget) - 1, widget.GetLeft());
			}

			RepositionMiddleAlignedWidgets();
		}

		private void RepositionLeftAlignedWidgets(int fromIndex, int fromX)
		{
			for (var i = fromIndex; i < leftAlignedWidgets.Length; i++)
			{
				var w = leftAlignedWidgets[i];
				w.RepositionControls(fromX, -1);
				fromX = w.GetRight();
			}

			rightmostLeftAlign = fromX;
		}

		private void RepositionRightAlignedWidgets(int fromIndex, int toX)
		{
			for (var i = fromIndex; i >= 0; i--)
			{
				var w = rightAlignedWidgets[i];
				w.RepositionControls(-1, toX);
				toX = w.GetLeft();
			}

			leftmostRightAlign = toX;
		}

		private void RepositionMiddleAlignedWidgets()
		{
			if (middleAlignedWidgets.Length > 0)
			{
				var eachWidth = (leftmostRightAlign - rightmostLeftAlign) / middleAlignedWidgets.Length;
				var x = rightmostLeftAlign;
				foreach (var w in middleAlignedWidgets)
				{
					w.RepositionControls(x, x + eachWidth);
					x += eachWidth;
				}
			}
		}

		private void PlaceControls()
		{
			this.form.SuspendLayout();

			var x = 0;
			foreach (var controls in this.leftAlignedWidgets.Select(widget => widget.GetControls(x, -1)))
			{
				controls.ForEach(this.form.Controls.Add);
				x = controls.FirstOrDefault() != null ? controls.Max(c => c.Right) : x;
			}
			rightmostLeftAlign = x;

			x = this.form.Size.Width;
			foreach (var controls in this.rightAlignedWidgets.Reverse().Select(widget => widget.GetControls(-1, x)))
			{
				controls.ForEach(this.form.Controls.Add);
				x = controls.FirstOrDefault() != null ? controls.Min(c => c.Left) : x;
			}
			leftmostRightAlign = x;

			if (middleAlignedWidgets.Length > 0)
			{
				var eachWidth = (leftmostRightAlign - rightmostLeftAlign) / middleAlignedWidgets.Length;
				x = rightmostLeftAlign;
				foreach (var controls in this.middleAlignedWidgets.Select(widget => widget.GetControls(x, x + eachWidth)))
				{
					controls.ForEach(this.form.Controls.Add);
					x += eachWidth;
				}
			}

			this.form.ResumeLayout();
		}

		public Label CreateLabel(string text, int xLocation, int width = -1)
		{
			var label = new Label();
			label.SuspendLayout();
			label.AutoSize = false;
			label.AutoEllipsis = true;
			label.Text = text;
			label.Font = font;
			label.Size = new Size(width == -1 ? TextRenderer.MeasureText(label.Text, label.Font).Width : width, this.barHeight);
			label.Location = new Point(xLocation, 0);
			label.TextAlign = ContentAlignment.MiddleLeft; // TODO: this doesn't work when there are ellipsis
			label.ResumeLayout();

			return label;
		}
	}
}
