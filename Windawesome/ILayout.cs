
namespace Windawesome
{
	public interface ILayout
	{
		string LayoutSymbol();

		string LayoutName();

		void Initialize(Workspace workspace);

		/*
		 * Should return whether Windawesome should restore a shared window's position if there
		 * are no changes to the workspace. If there are, Reposition will be called anyway
		 */
		bool ShouldSaveAndRestoreSharedWindowsPosition();

		void Reposition();

		void WindowMinimized(Window window);

		void WindowRestored(Window window);

		void WindowCreated(Window window);

		void WindowDestroyed(Window window);
	}
}
