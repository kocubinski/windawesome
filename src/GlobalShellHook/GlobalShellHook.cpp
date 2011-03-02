// GlobalShellHook.cpp : Defines the exported functions for the DLL application.
//

#include "stdafx.h"
#include "GlobalShellHook.h"

#pragma data_seg(".shared")
#pragma comment(linker, "/SECTION:.shared,RWS")

HWND applicationHandle = NULL;
UINT MESSAGE_ARA = 0;

#pragma data_seg()

HINSTANCE hInstance;
HHOOK hook;

static LRESULT CALLBACK ShellHookProc(int code, WPARAM wParam, LPARAM lParam);

void RegisterGlobalShellHook(HWND handle)
{
	MESSAGE_ARA = RegisterWindowMessage(TEXT("MESSAGE_ARA"));
	hook = SetWindowsHookEx(WH_SHELL, (HOOKPROC) ShellHookProc, hInstance, 0);
	applicationHandle = handle;
}

void UnregisterGlobalShellHook()
{
	UnhookWindowsHookEx(hook);
}

static LRESULT CALLBACK ShellHookProc(int code, WPARAM wParam, LPARAM lParam)
{
	if (code >= 0)
	{
		PostMessage(applicationHandle, MESSAGE_ARA, wParam, code);

		return 0;
	}

	return CallNextHookEx(NULL, code, wParam, lParam);
}