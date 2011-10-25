using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using IronPython.Hosting;
using IronRuby;
using Microsoft.Scripting.Hosting;

namespace Windawesome
{
	public class Config
	{
		public IEnumerable<IPlugin> Plugins { get; set; }
		public IBar[] Bars { get; set; }
		public Workspace[] Workspaces { get; set; }
		public IEnumerable<Workspace> StartingWorkspaces { get; set; }
		public IEnumerable<ProgramRule> ProgramRules { get; set; }

		public int WindowBorderWidth { get; set; }
		public int WindowPaddedBorderWidth { get; set; }
		public bool ShowMinimizeMaximizeRestoreAnimations { get; set; }
		public bool HideMouseWhenTyping { get; set; }
		public bool FocusFollowsMouse { get; set; }
		public bool FocusFollowsMouseSetOnTop { get; set; }
		public bool MoveMouseOverMonitorsOnSwitch { get; set; }
		public Tuple<NativeMethods.MOD, System.Windows.Forms.Keys> UniqueHotkey { get; set; }

		internal Config()
		{
			this.WindowBorderWidth = -1;
			this.WindowPaddedBorderWidth = -1;
		}

		internal void LoadConfiguration(Windawesome windawesome)
		{
			const string layoutsDirName = "Layouts";
			const string widgetsDirName = "Widgets";
			const string pluginsDirName = "Plugins";
			const string configDirName  = "Config";

			if (!Directory.Exists(configDirName) || Directory.EnumerateFiles(configDirName).FirstOrDefault() == null)
			{
				throw new Exception("You HAVE to have a " + configDirName + " directory in the folder and it must " +
					"contain at least one Python or Ruby file that initializes all instance variables in 'config' " +
					"that don't have default values!");
			}
			if (!Directory.Exists(layoutsDirName))
			{
				Directory.CreateDirectory(layoutsDirName);
			}
			if (!Directory.Exists(widgetsDirName))
			{
				Directory.CreateDirectory(widgetsDirName);
			}
			if (!Directory.Exists(pluginsDirName))
			{
				Directory.CreateDirectory(pluginsDirName);
			}
			var files =
				Directory.EnumerateFiles(layoutsDirName).Select(fileName => new FileInfo(fileName)) .Concat(
				Directory.EnumerateFiles(widgetsDirName).Select(fileName => new FileInfo(fileName))).Concat(
				Directory.EnumerateFiles(pluginsDirName).Select(fileName => new FileInfo(fileName))).Concat(
				Directory.EnumerateFiles(configDirName) .Select(fileName => new FileInfo(fileName)));

			PluginLoader.LoadAll(windawesome, this, files);
		}

		private static class PluginLoader
		{
			private static ScriptEngine pythonEngine;
			private static ScriptEngine rubyEngine;

			private static ScriptEngine PythonEngine
			{
				get
				{
					if (pythonEngine == null)
					{
						pythonEngine = Python.CreateEngine();
						InitializeScriptEngine(pythonEngine);
					}
					return pythonEngine;
				}
			}

			private static ScriptEngine RubyEngine
			{
				get
				{
					if (rubyEngine == null)
					{
						rubyEngine = Ruby.CreateEngine();
						InitializeScriptEngine(rubyEngine);
					}
					return rubyEngine;
				}
			}

			private static void InitializeScriptEngine(ScriptEngine engine)
			{
				var searchPaths = engine.GetSearchPaths().ToList();
				searchPaths.Add(Environment.CurrentDirectory);
				engine.SetSearchPaths(searchPaths);

				AppDomain.CurrentDomain.GetAssemblies().ForEach(engine.Runtime.LoadAssembly);
			}

			private static ScriptEngine GetEngineForFile(FileSystemInfo file)
			{
				switch (file.Extension)
				{
					case ".ir":
					case ".rb":
						return RubyEngine;
					case ".ipy":
					case ".py":
						return PythonEngine;
					case ".dll":
						var assembly = Assembly.LoadFrom(file.FullName);
						if (rubyEngine != null)
						{
							rubyEngine.Runtime.LoadAssembly(assembly);
						}
						if (pythonEngine != null)
						{
							pythonEngine.Runtime.LoadAssembly(assembly);
						}
						break;
				}

				return null;
			}

			public static void LoadAll(Windawesome windawesome, Config config, IEnumerable<FileInfo> files)
			{
				ScriptScope scope = null;
				ScriptEngine previousLanguage = null;
				foreach (var file in files)
				{
					var engine = GetEngineForFile(file);
					if (engine != null)
					{
						if (scope == null)
						{
							scope = engine.CreateScope();
							scope.SetVariable("windawesome", windawesome);
							scope.SetVariable("config", config);
						}
						else if (previousLanguage != engine)
						{
							var oldScope = scope;
							scope = engine.CreateScope();
							oldScope.GetItems().
								Where(variable => variable.Value != null).
								ForEach(variable => scope.SetVariable(variable.Key, variable.Value));
							previousLanguage.Runtime.Globals.GetItems().
								Where(variable => variable.Value != null).
								ForEach(variable => scope.SetVariable(variable.Key, variable.Value));
						}

						scope = engine.ExecuteFile(file.FullName, scope);
						previousLanguage = engine;
					}
				}
			}
		}
	}

	public enum State
	{
		SHOWN  = 0,
		HIDDEN = 1,
		AS_IS  = 2
	}

	public enum OnWindowCreatedOnCurrentWorkspaceAction
	{
		ActivateWindow,
		MoveToBottom
	}

	public enum OnWindowShownAction
	{
		SwitchToWindowsWorkspace,
		MoveWindowToCurrentWorkspace,
		TemporarilyShowWindowOnCurrentWorkspace,
		HideWindow
	}

	public class ProgramRule
	{
		internal readonly Regex className;
		internal readonly Regex displayName;
		internal readonly Regex processName;
		internal readonly NativeMethods.WS styleContains;
		internal readonly NativeMethods.WS styleNotContains;
		internal readonly NativeMethods.WS_EX exStyleContains;
		internal readonly NativeMethods.WS_EX exStyleNotContains;
		private readonly CustomMatchingFunction customMatchingFunction;
		internal readonly CustomMatchingFunction customOwnedWindowMatchingFunction;

		internal readonly bool isManaged;
		internal readonly int tryAgainAfter;
		internal readonly int windowCreatedDelay;
		internal readonly bool redrawDesktopOnWindowCreated;
		internal readonly bool showMenu;
		internal readonly OnWindowCreatedOnCurrentWorkspaceAction onWindowCreatedOnCurrentWorkspaceAction;
		internal readonly OnWindowShownAction onWindowCreatedAction;
		internal readonly OnWindowShownAction onHiddenWindowShownAction;
		internal readonly Rule[] rules;

		public delegate bool CustomMatchingFunction(IntPtr hWnd);

		// http://blogs.msdn.com/b/oldnewthing/archive/2007/10/08/5351207.aspx
		private static bool DefaultMatchingFunction(IntPtr hWnd)
		{
			return !NativeMethods.GetWindowExStyleLongPtr(hWnd).HasFlag(NativeMethods.WS_EX.WS_EX_TOOLWINDOW) &&
				NativeMethods.GetWindow(hWnd, NativeMethods.GW.GW_OWNER) == IntPtr.Zero;
		}

		private static bool DefaultOwnedWindowMatchingFunction(IntPtr hWnd)
		{
			return !NativeMethods.GetWindowExStyleLongPtr(hWnd).HasFlag(NativeMethods.WS_EX.WS_EX_TOOLWINDOW);
		}

		internal bool IsMatch(IntPtr hWnd, string cName, string dName, string pName, NativeMethods.WS style, NativeMethods.WS_EX exStyle)
		{
			return className.IsMatch(cName) && displayName.IsMatch(dName) && processName.IsMatch(pName) &&
				(style & styleContains) == styleContains && (style & styleNotContains) == 0 &&
				(exStyle & exStyleContains) == exStyleContains && (exStyle & exStyleNotContains) == 0 &&
				customMatchingFunction(hWnd);
		}

		public ProgramRule(string className = ".*", string displayName = ".*", string processName = ".*",
			NativeMethods.WS styleContains = (NativeMethods.WS) 0, NativeMethods.WS styleNotContains = (NativeMethods.WS) 0,
			NativeMethods.WS_EX exStyleContains = (NativeMethods.WS_EX) 0, NativeMethods.WS_EX exStyleNotContains = (NativeMethods.WS_EX) 0,
			CustomMatchingFunction customMatchingFunction = null,

			bool isManaged = true, int tryAgainAfter = -1, int windowCreatedDelay = -1, bool redrawDesktopOnWindowCreated = false, bool showMenu = true,
			OnWindowCreatedOnCurrentWorkspaceAction onWindowCreatedOnCurrentWorkspaceAction = OnWindowCreatedOnCurrentWorkspaceAction.ActivateWindow,
			OnWindowShownAction onWindowCreatedAction = OnWindowShownAction.SwitchToWindowsWorkspace,
			OnWindowShownAction onHiddenWindowShownAction = OnWindowShownAction.SwitchToWindowsWorkspace,
			int showOnWorkspacesCount = 0, IEnumerable<Rule> rules = null)
		{
			this.className = new Regex(className, RegexOptions.Compiled);
			this.displayName = new Regex(displayName, RegexOptions.Compiled);
			this.processName = new Regex(processName, RegexOptions.Compiled);
			this.styleContains = styleContains;
			this.styleNotContains = styleNotContains;
			this.exStyleContains = exStyleContains;
			this.exStyleNotContains = exStyleNotContains;
			this.customMatchingFunction = customMatchingFunction ?? DefaultMatchingFunction;
			this.customOwnedWindowMatchingFunction = DefaultOwnedWindowMatchingFunction;

			this.isManaged = isManaged;
			if (isManaged)
			{
				this.tryAgainAfter = tryAgainAfter;
				this.windowCreatedDelay = windowCreatedDelay;
				this.redrawDesktopOnWindowCreated = redrawDesktopOnWindowCreated;
				this.showMenu = showMenu;
				this.onWindowCreatedOnCurrentWorkspaceAction = onWindowCreatedOnCurrentWorkspaceAction;
				this.onWindowCreatedAction = onWindowCreatedAction;
				this.onHiddenWindowShownAction = onHiddenWindowShownAction;

				if (showOnWorkspacesCount > 0)
				{
					if (rules == null)
					{
						rules = new Rule[] { };
					}

					// This is slow (n ^ 2), but it doesn't matter in this case
					this.rules = rules.Concat(
						Enumerable.Range(1, showOnWorkspacesCount).Where(i => rules.All(r => r.workspace != i)).Select(i => new Rule(i))).
						ToArray();
				}
				else
				{
					this.rules = rules == null ? new[] { new Rule() } : rules.ToArray();
				}
			}
		}

		public class Rule
		{
			public Rule(int workspace = 0, bool isFloating = false,
				State titlebar = State.AS_IS, State inAltTabAndTaskbar = State.AS_IS, State windowBorders = State.AS_IS,
				bool redrawOnShow = false, bool hideFromAltTabAndTaskbarWhenOnInactiveWorkspace = false)
			{
				this.workspace = workspace;
				this.isFloating = isFloating;
				this.titlebar = titlebar;
				this.inAltTabAndTaskbar = inAltTabAndTaskbar;
				this.windowBorders = windowBorders;
				this.redrawOnShow = redrawOnShow;
				this.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace = hideFromAltTabAndTaskbarWhenOnInactiveWorkspace;
			}

			internal readonly int workspace;
			internal readonly bool isFloating;
			internal readonly State titlebar;
			internal readonly State inAltTabAndTaskbar;
			internal readonly State windowBorders;
			internal readonly bool redrawOnShow;
			internal readonly bool hideFromAltTabAndTaskbarWhenOnInactiveWorkspace;
		}
	}
}
