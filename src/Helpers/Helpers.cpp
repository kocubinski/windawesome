
#include "stdafx.h"
#include "Helpers.h"

//BOOL IsAppWindow(HWND hWnd)
//{
//	if (IsWindowVisible(hWnd) && !(GetWindowLongPtr(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) && !GetParent(hWnd))
//	{
//		HWND hWndOwner = GetWindow(hWnd, GW_OWNER);
//		if (!hWndOwner || (IsWindowVisible(hWndOwner) && GetWindowLongPtr(hWndOwner, GWL_EXSTYLE) & WS_EX_TOOLWINDOW))
//		{
//			return TRUE;
//		}
//	}
//
//	return FALSE;
//}

BOOL IsAppWindow(HWND hWnd)
{
	if (IsWindowVisible(hWnd) && !GetParent(hWnd))
	{
		HWND hWndOwner = GetWindow(hWnd, GW_OWNER);
		LONG_PTR exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
		if ((!(exStyle & WS_EX_TOOLWINDOW) && !hWndOwner) ||
			( (exStyle & WS_EX_APPWINDOW)  && hWndOwner))
		{
			return TRUE;
		}
	}

	return FALSE;
}

//BOOL IsAppWindow(HWND hWnd)
//{
//	LONG_PTR exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE);
//	if (IsWindowVisible(hWnd) && !GetParent(hWnd) &&
//		(exStyle & WS_EX_APPWINDOW ||
//		(!(exStyle & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) && !GetWindow(hWnd, GW_OWNER))))
//	{
//		return TRUE;
//	}
//
//	return FALSE;
//}

//BOOL IsAppWindow(HWND hWnd)
//{
//	return IsWindowVisible(hWnd) && !(GetWindowLongPtr(hWnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) && !GetParent(hWnd);
//}

void ForceForegroundWindow(HWND hWnd)
{
	HWND foregroundWindow = GetForegroundWindow();
	if (foregroundWindow == hWnd)
	{
		return ;
	}
	if (!foregroundWindow)
	{
		SetForegroundWindow(hWnd);
	}
	else
	{
		DWORD foregroundThreadID = GetWindowThreadProcessId(foregroundWindow, NULL);
		DWORD currentThreadID = GetCurrentThreadId();
		if (currentThreadID == foregroundThreadID)
		{
			SetForegroundWindow(hWnd);
		}
		else if (AttachThreadInput(currentThreadID, foregroundThreadID, true))
		{
			int i;
			for (i = 0; i < 5; i++)
			{
				if (SetForegroundWindow(hWnd))
				{
					break;
				}
			}
			if (i == 5)
			{
				DWORD targetThreadID = GetWindowThreadProcessId(hWnd, NULL);
				if (AttachThreadInput(foregroundThreadID, targetThreadID, true))
				{
					for (i = 0; i < 5; i++)
					{
						if (SetForegroundWindow(hWnd))
						{
							break;
						}
					}
					if (i == 5)
					{
						INPUT inp[4];
						ZeroMemory(&inp, sizeof(inp));
						inp[0].type = inp[1].type = inp[2].type = inp[3].type = INPUT_KEYBOARD;
						inp[0].ki.wVk = inp[1].ki.wVk = inp[2].ki.wVk = inp[3].ki.wVk = VK_MENU;
						inp[0].ki.dwFlags = inp[2].ki.dwFlags = KEYEVENTF_EXTENDEDKEY;
						inp[1].ki.dwFlags = inp[3].ki.dwFlags = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP;
						SendInput(4, inp, sizeof(INPUT));

						SetForegroundWindow(hWnd);
					}
					AttachThreadInput(foregroundThreadID, targetThreadID, false);
				}
			}
			AttachThreadInput(currentThreadID, foregroundThreadID, false);
		}
	}
}

static IShellDispatch2 *psd = NULL;

void RunApplicationNonElevated(const WCHAR* path, const WCHAR* arguments)
{
	if (psd == NULL)
	{
		IShellWindows *psw;
		if (!SUCCEEDED(CoCreateInstance(CLSID_ShellWindows, NULL, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&psw))))
		{
			return ;
		}
		HWND hwnd;
		IDispatch* pdisp;
		VARIANT vEmpty = {}; // VT_EMPTY
		if (S_OK != psw->FindWindowSW(&vEmpty, &vEmpty, SWC_DESKTOP, (long*) &hwnd, SWFO_NEEDDISPATCH, &pdisp))
		{
			return ;
		}
		IShellBrowser *psb;

		if (!SUCCEEDED(IUnknown_QueryService(pdisp, SID_STopLevelBrowser, IID_PPV_ARGS(&psb))))
		{
			return ;
		}
		IShellView *psv;
		psb->QueryActiveShellView(&psv);

		IDispatch *pdispBackground;
		if (!SUCCEEDED(psv->GetItemObject(SVGIO_BACKGROUND, IID_PPV_ARGS(&pdispBackground))))
		{
			return ;
		}
		IShellFolderViewDual *psfvd;
		if (!SUCCEEDED(pdispBackground->QueryInterface(IID_PPV_ARGS(&psfvd))))
		{
			return ;
		}
		if (!SUCCEEDED(psfvd->get_Application(&pdisp)))
		{
			return ;
		}
		if (!SUCCEEDED(pdisp->QueryInterface(IID_PPV_ARGS(&psd))))
		{
			return ;
		}
	}

	VARIANT args, dir, operation, show;
	args.bstrVal = SysAllocString(arguments);
	args.vt = VT_BSTR;
	dir.bstrVal = args.bstrVal;
	dir.vt = VT_BSTR;
	operation.bstrVal = SysAllocString(L"open");
	operation.vt = VT_BSTR;
	show.intVal = 10;
	show.vt = VT_INT;
	BSTR p = SysAllocString(path);
	psd->ShellExecuteW(p, args, dir, operation, show);
}
