
#include "stdafx.h"
#include "SystemTrayHook.h"

#pragma data_seg(".shared")
#pragma comment(linker, "/SECTION:.shared,RWS")

HWND applicationHandle = NULL;

#pragma data_seg()

static HHOOK hook = NULL;
HINSTANCE hInstance = NULL;

static LRESULT CALLBACK HookCallback(int code, WPARAM wParam, LPARAM lParam);

BOOL RegisterSystemTrayHook(HWND hWnd)
{
	HWND hShell = FindWindow(L"Shell_TrayWnd", NULL);
	DWORD shellThread = GetWindowThreadProcessId(hShell, NULL);

	applicationHandle = hWnd;

	return (hook = SetWindowsHookEx(WH_CALLWNDPROCRET, (HOOKPROC) HookCallback, hInstance, shellThread)) != NULL;
}

BOOL UnregisterSystemTrayHook()
{
	return hook != NULL ? UnhookWindowsHookEx(hook) : TRUE;
}

#define SH_TRAY_DATA 1

typedef struct
{
	DWORD dwHz;
	DWORD dwMessage;
	NOTIFYICONDATA nid; // this is a 64-bit structure, when running in 64-bit mode, but should be 32!
} SHELLTRAYDATA;

static LRESULT CALLBACK HookCallback(int code, WPARAM wParam, LPARAM lParam)
{
	if (code >= 0)
	{
		CWPRETSTRUCT *pInfo = (CWPRETSTRUCT*) lParam;

		if (pInfo->message == WM_COPYDATA)
		{
			COPYDATASTRUCT* copyDataStruct = (COPYDATASTRUCT*) pInfo->lParam;
			if (copyDataStruct->dwData == SH_TRAY_DATA &&
				((SHELLTRAYDATA*) copyDataStruct->lpData)->dwHz == 0x34753423)
			{
				SendMessageTimeout(applicationHandle, WM_COPYDATA, pInfo->wParam, pInfo->lParam,
					SMTO_ABORTIFHUNG, 10000, NULL);
			}
		}
	}

	return CallNextHookEx(NULL, code, wParam, lParam);
}
