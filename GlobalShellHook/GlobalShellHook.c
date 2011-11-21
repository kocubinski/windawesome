// GlobalShellHook.cpp : Defines the exported functions for the DLL application.
//

#include "stdafx.h"
#include "GlobalShellHook.h"

#pragma data_seg(".shared")
#pragma comment(linker, "/SECTION:.shared,RWS")

HWND applicationHandle = NULL;
UINT globalShellHookMessage = 0;

#pragma data_seg()

HINSTANCE hInstance = NULL;
static HHOOK hook = NULL;

static LRESULT CALLBACK ShellHookProc(int code, WPARAM wParam, LPARAM lParam);

BOOL RegisterGlobalShellHook(HWND hWnd)
{
	applicationHandle = hWnd;

	globalShellHookMessage = RegisterWindowMessage(TEXT("GLOBAL_SHELL_HOOK"));

	return (hook = SetWindowsHookEx(WH_SHELL, (HOOKPROC) ShellHookProc, hInstance, 0)) != NULL;
}

BOOL UnregisterGlobalShellHook()
{
	return hook != NULL ? UnhookWindowsHookEx(hook) : TRUE;
}

static LRESULT CALLBACK ShellHookProc(int code, WPARAM wParam, LPARAM lParam)
{
	if (code == HSHELL_LANGUAGE)
	{
		PostMessage(applicationHandle, globalShellHookMessage, wParam, lParam);
	}

	return CallNextHookEx(NULL, code, wParam, lParam);
}
