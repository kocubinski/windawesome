
#include "stdafx.h"
#include "WindowSubclassing.h"

#pragma data_seg(".shared")
#pragma comment(linker, "/SECTION:.shared,RWS")

HWND applicationHandle = NULL;

UINT START_WINDOW_PROC_MESSAGE = 0;
UINT STOP_WINDOW_PROC_MESSAGE = 0;

#pragma data_seg()

HINSTANCE hInstance;
static WNDPROC oldWndProc = NULL;
static BOOL isListening;

static LRESULT CALLBACK CallWndRetProc(int code, WPARAM wParam, LPARAM lParam);
static LRESULT CALLBACK WindowProc(HWND hwnd, UINT uMsg, WPARAM wParam, LPARAM lParam);

BOOL SubclassWindow(HWND hWnd, HWND window)
{
	HHOOK hook = SetWindowsHookEx(WH_CALLWNDPROCRET, (HOOKPROC) CallWndRetProc, hInstance, GetWindowThreadProcessId(window, NULL));
	if (!hook)
	{
		return FALSE;
	}
	applicationHandle = hWnd;
	SendMessage(window, START_WINDOW_PROC_MESSAGE, 0, 0);
	UnhookWindowsHookEx(hook);
	return TRUE;
}

BOOL UnsubclassWindow(HWND window)
{
	HHOOK hook = SetWindowsHookEx(WH_CALLWNDPROCRET, (HOOKPROC) CallWndRetProc, hInstance, GetWindowThreadProcessId(window, NULL));
	if (!hook)
	{
		return FALSE;
	}
	SendMessage(window, STOP_WINDOW_PROC_MESSAGE, 0, 0);
	UnhookWindowsHookEx(hook);
	return TRUE;
}

static LRESULT CALLBACK CallWndRetProc(int code, WPARAM wParam, LPARAM lParam)
{
	if (code >= 0)
	{
		if (((CWPRETSTRUCT*) lParam)->message == START_WINDOW_PROC_MESSAGE)
		{
			WCHAR library[MAX_PATH];
			GetModuleFileName(hInstance, library, MAX_PATH);
			LoadLibrary(library);

			isListening = FALSE; // because on the current workspace the window may need to be movable/resizable
			oldWndProc = (WNDPROC) SetWindowLongPtr(((CWPRETSTRUCT*) lParam)->hwnd, GWLP_WNDPROC, (LONG_PTR) WindowProc);
			if (!oldWndProc)
			{
				FreeLibrary(hInstance);
			}
			return TRUE;
		}
		if (((CWPRETSTRUCT*) lParam)->message == STOP_WINDOW_PROC_MESSAGE)
		{
			if (oldWndProc && SetWindowLongPtr(((CWPRETSTRUCT*) lParam)->hwnd, GWLP_WNDPROC, (LONG_PTR) oldWndProc))
			{
				FreeLibrary(hInstance);
				oldWndProc = NULL;
			}
			return TRUE;
		}
	}

	return CallNextHookEx(NULL, code, wParam, lParam);
}

static LRESULT CALLBACK WindowProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
	if (msg == START_WINDOW_PROC_MESSAGE)
	{
		isListening = TRUE;
		return TRUE;
	}
	if (isListening)
	{
		if (msg == STOP_WINDOW_PROC_MESSAGE)
		{
			isListening = FALSE;
			return TRUE;
		}
		switch (msg)
		{
			case WM_MOVING:
			case WM_SIZING:
				GetWindowRect(hwnd, (RECT*) lParam);
				return TRUE;
			case WM_NCLBUTTONDBLCLK:
				if (wParam == HTCAPTION)
				{
					return 0;
				}
				break;
			case WM_SYSCOMMAND:
				if (wParam == 0xF012 || wParam == SC_RESTORE || wParam == SC_MOVE || wParam == SC_SIZE)
				{
					return 0;
				}
		}
	}

	return CallWindowProc(oldWndProc, hwnd, msg, wParam, lParam);
}
