using System.Collections.Generic;
using System.Windows.Forms;

namespace Windawesome
{
	public interface IWidget
	{
		/*
		 * This is guaranteed to be called exactly once for every Widget type. Useful for
		 * initializing any static data
		 */
		void StaticInitializeWidget(Windawesome windawesome);

		/*
		 * This is called for every instance of the Widget and is given its Bar
		 */
		void InitializeWidget(Bar bar);
		
		void RepositionControls(int left, int right);

		/*
		 * One should save left and right from the values in RepositionControls
		 * as the Bar can request them at any point. Even more - the Bar gives -1 for one of the
		 * two values if the Widget is with FixedWidth, but the Bar will expect valid values for
		 * both, so make sure you save and initialize them properly
		 */
		int GetLeft();

		int GetRight();

		/*
		 * This is guaranteed to be called exactly once for every Widget type. Useful for
		 * disposing of some shared resources
		 */
		void StaticDispose();

		/*
		 * This is called for every instance of the Widget and is given its Bar
		 */
		void Dispose();

		void Refresh();
	}

	public interface ISpanWidget : IWidget
	{
		/*
		 * GetInitialControls is called only once in the beginning. After that,
		 * when any changes occur, RepositionControls will be called
		 */
		IEnumerable<Control> GetInitialControls();
	}

	public interface IFixedWidthWidget : IWidget
	{
		/*
		 * GetInitialControls is called only once in the beginning. After that,
		 * when any changes occur, RepositionControls will be called
		 * 
		 * The argument isLeft specifies whether the Widget is on the left or the right side of the Bar
		 */
		IEnumerable<Control> GetInitialControls(bool isLeft);
	}
}
