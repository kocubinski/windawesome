using System.Collections.Generic;
using System.Windows.Forms;

namespace Windawesome
{
	public enum WidgetType
	{
		FixedWidth,
		Span
	}

	public interface IWidget
	{
		WidgetType GetWidgetType();

		/*
		 * This is guaranteed to be called exactly once for every widget type. Useful for
		 * initializing any static data
		 */
		void StaticInitializeWidget(Windawesome windawesome, Config config);

		/*
		 * This is called for every Bar that contains this instance of the widget
		 */
		void InitializeWidget(Bar bar);

		/*
		 * If the type is FixedWidth, then only one of the parameters "left" and "right" will be
		 * different from -1. If it is "left", then the controls must BEGIN from
		 * that point. If it is "right", then the controls must END at that point
		 *
		 * If the type is Span, then both parameters have valid values and the controls should be
		 * position in between
		 *
		 * Widgets should take care if there isn't enough space to put their controls, i.e. they
		 * have reached the end/beginning of the monitor's working area
		 *
		 * GetControls is called only once, when the initial positioning takes place. After that,
		 * when any changes occur, RepositionControls will be called
		 */
		IEnumerable<Control> GetControls(int left, int right);

		void RepositionControls(int left, int right);

		/*
		 * One should save left and right from the values in GetControls and RepositionControls
		 * as the Bar can request them at any point. Even more - the Bar gives -1 for one of the
		 * two values if the Widget is with FixedWidth, but the Bar will expect valid values for
		 * both, so make sure you save and initialize them properly
		 */
		int GetLeft();

		int GetRight();

		void WidgetShown();

		void WidgetHidden();

		/*
		 * This is guaranteed to be called exactly once for every widget type. Useful for
		 * disposing of some shared resources
		 */
		void StaticDispose();

		/*
		 * This is called for every Bar that contains this instance of the widget
		 */
		void Dispose();
	}
}
