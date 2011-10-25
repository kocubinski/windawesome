
namespace Windawesome
{
	public interface ILayout
	{
		string LayoutSymbol();
		string LayoutName();

		void Initialize(Workspace workspace);
		void Dispose();

		/*
		 * Should return whether Windawesome should restore a shared window's position if there
		 * are no changes to the workspace. If there are, Reposition will be called anyway
		 */
		bool ShouldSaveAndRestoreSharedWindowsPosition();

		/*
		 * This is guaranteed to be called only when the workspace is visible
		 */
		void Reposition();

		/*
		 * The next four functions can be called when the workspace is visible, as well
		 * as invisible
		 */
		void WindowMinimized(Window window);
		void WindowRestored(Window window);
		void WindowCreated(Window window);
		void WindowDestroyed(Window window);
	}
}
