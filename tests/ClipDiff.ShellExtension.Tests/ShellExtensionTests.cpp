#include "../../src/ClipDiff.ShellExtension/ShellExtension.h"
#include <wrl/client.h>
#include <atomic>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <iterator>
#include <optional>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace fs = std::filesystem;

namespace
{
    const char* currentCase = "startup";

    void BeginCase(const char* name)
    {
        currentCase = name;
        std::cout << "[RUN] " << name << '\n';
    }

    std::string Hex(DWORD value)
    {
        std::ostringstream text;
        text << "0x" << std::hex << std::uppercase << std::setw(8) << std::setfill('0') << value;
        return text.str();
    }

    std::string WindowsError(DWORD code)
    {
        char buffer[1024]{};
        const DWORD length = FormatMessageA(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr, code, 0, buffer, static_cast<DWORD>(sizeof(buffer)), nullptr);
        std::string message(buffer, length);
        const auto end = message.find_last_not_of("\r\n ");
        if (end != std::string::npos) message.resize(end + 1);
        return Hex(code) + (message.empty() ? " (no system message)" : " (" + message + ")");
    }

    void Check(bool condition, const char* message)
    {
        if (!condition) throw std::runtime_error(message);
    }
    void CheckHr(HRESULT result, const char* message)
    {
        if (FAILED(result))
            throw std::runtime_error(std::string(message) + " HRESULT=" + WindowsError(static_cast<DWORD>(result)));
    }

    void CheckWin32(BOOL succeeded, const char* message)
    {
        if (!succeeded)
        {
            const DWORD error = GetLastError();
            throw std::runtime_error(std::string(message) + " Win32=" + WindowsError(error));
        }
    }

    void CheckRegistry(LSTATUS result, const char* message)
    {
        if (result != ERROR_SUCCESS)
            throw std::runtime_error(std::string(message) + " Registry=" + WindowsError(result));
    }

    // Read only these diagnostic values, never selected paths or clipboard data. Missing values and
    // denied reads are reported separately; diagnostic probes must not replace the original failure.
    void PrintRegistryValue(HKEY root, const char* rootName, const wchar_t* key, const wchar_t* name,
        const char* label, bool presenceOnly = false, const wchar_t* expected = nullptr)
    {
        wchar_t text[32768]{};
        DWORD type = 0;
        DWORD bytes = sizeof(text);
        const LSTATUS result = RegGetValueW(root, key, name, RRF_RT_ANY | RRF_NOEXPAND,
            &type, presenceOnly ? nullptr : text, &bytes);
        std::cout << "  " << rootName << ' ' << label << "=";
        if (result == ERROR_FILE_NOT_FOUND || result == ERROR_PATH_NOT_FOUND) std::cout << "absent";
        else if (result != ERROR_SUCCESS) std::cout << "unreadable: " << WindowsError(result);
        else if (presenceOnly) std::cout << "present";
        else if (expected)
            std::cout << ((type == REG_SZ && std::wstring(text) == expected) ? "matches test registration" : "MISMATCH");
        else if (type == REG_DWORD && bytes == sizeof(DWORD))
        {
            DWORD value = 0;
            CopyMemory(&value, text, sizeof(value));
            std::cout << value << " (" << Hex(value) << ')';
        }
        else std::cout << "unexpected registry type=" << type << ", bytes=" << bytes;
        std::cout << '\n';
    }

    void PrintShellRestrictions()
    {
        std::cout << "Shell restriction snapshot (read-only; presence alone does not prove the cause):\n";
        const wchar_t* clsid = L"{6B46A974-40E2-4AD4-9F68-E534202B11E8}";
        for (const auto root : {HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE})
        {
            const char* name = root == HKEY_CURRENT_USER ? "HKCU" : "HKLM";
            PrintRegistryValue(root, name, L"Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer",
                L"EnforceShellExtensionSecurity", "Policies/Explorer/EnforceShellExtensionSecurity");
            PrintRegistryValue(root, name, L"Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer",
                L"NoViewContextMenu", "Policies/Explorer/NoViewContextMenu");
            PrintRegistryValue(root, name, L"Software\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Approved",
                clsid, "Shell Extensions/Approved/ClipDiff", true);
            PrintRegistryValue(root, name, L"Software\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Blocked",
                clsid, "Shell Extensions/Blocked/ClipDiff", true);
        }
    }

    void PrintSelection(IDataObject* data, HANDLE ready)
    {
        const DWORD readyState = WaitForSingleObject(ready, 0);
        const DWORD waitError = readyState == WAIT_FAILED ? GetLastError() : ERROR_SUCCESS;
        std::cout << "  Readiness event wait=" << Hex(readyState) << " (0=ready)";
        if (readyState == WAIT_FAILED) std::cout << ", Win32=" << WindowsError(waitError);
        std::cout << '\n';
        FORMATETC format{CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
        std::cout << "  CF_HDROP QueryGetData=" << Hex(static_cast<DWORD>(data->QueryGetData(&format))) << '\n';
        ComPtr<IShellItemArray> items;
        HRESULT result = SHCreateShellItemArrayFromDataObject(data, IID_PPV_ARGS(&items));
        std::cout << "  SHCreateShellItemArrayFromDataObject=" << WindowsError(static_cast<DWORD>(result)) << '\n';
        if (FAILED(result)) return;
        DWORD count = 0;
        result = items->GetCount(&count);
        std::cout << "  GetCount=" << Hex(static_cast<DWORD>(result)) << ", count=" << count << '\n';
        if (FAILED(result)) return;
        for (DWORD index = 0; index < count; ++index)
        {
            ComPtr<IShellItem> item;
            result = items->GetItemAt(index, &item);
            std::cout << "  Item[" << index << "] GetItemAt=" << Hex(static_cast<DWORD>(result));
            if (SUCCEEDED(result))
            {
                SFGAOF attributes = 0;
                result = item->GetAttributes(SFGAO_FILESYSTEM | SFGAO_FOLDER | SFGAO_STREAM, &attributes);
                std::cout << ", GetAttributes=" << Hex(static_cast<DWORD>(result)) << ", attributes=" << Hex(attributes)
                    << " (filesystem=" << !!(attributes & SFGAO_FILESYSTEM) << ", folder=" << !!(attributes & SFGAO_FOLDER)
                    << ", stream=" << !!(attributes & SFGAO_STREAM) << ')';
            }
            std::cout << '\n';
        }
    }

    struct Handle
    {
        HANDLE value = nullptr;
        ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    };

    std::optional<int> RunWithoutElevation(int argc, wchar_t** argv)
    {
        Handle token;
        CheckWin32(OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token.value), "Cannot inspect test process token.");
        TOKEN_ELEVATION elevation{};
        DWORD size = 0;
        CheckWin32(GetTokenInformation(token.value, TokenElevation, &elevation, sizeof(elevation), &size),
            "Cannot inspect test process elevation.");
        TOKEN_ELEVATION_TYPE elevationType{};
        CheckWin32(GetTokenInformation(token.value, TokenElevationType, &elevationType, sizeof(elevationType), &size),
            "Cannot inspect token elevation type.");
        std::cout << "Process: bits=" << sizeof(void*) * 8 << ", elevated=" << elevation.TokenIsElevated
            << ", elevationType=" << elevationType << " (1=default, 2=full, 3=limited)\n";
        if (!elevation.TokenIsElevated) return std::nullopt;
        Check(argc == 2, "Test child must run without elevation.");

        // Hosted Windows CI runs as administrator. Exercise HKCU Shell integration as a normal app.
        // Restrict only this test child; do not change the account, UAC, or machine security settings.
        Handle source;
        CheckWin32(OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY |
            TOKEN_ADJUST_DEFAULT, &source.value), "Cannot open administrator token for restriction.");
        Handle restricted;
        CheckWin32(CreateRestrictedToken(source.value, LUA_TOKEN | DISABLE_MAX_PRIVILEGE, 0, nullptr, 0, nullptr,
            0, nullptr, &restricted.value), "Cannot create non-administrator test token.");
        BYTE sid[SECURITY_MAX_SID_SIZE]{};
        DWORD sidSize = sizeof(sid);
        CheckWin32(CreateWellKnownSid(WinMediumLabelSid, nullptr, sid, &sidSize), "Cannot create medium integrity SID.");
        TOKEN_MANDATORY_LABEL label{};
        label.Label.Sid = sid;
        label.Label.Attributes = SE_GROUP_INTEGRITY;
        CheckWin32(SetTokenInformation(restricted.value, TokenIntegrityLevel, &label,
            static_cast<DWORD>(sizeof(label)) + sidSize), "Cannot lower test process integrity.");
        wchar_t executable[32768]{};
        const DWORD length = GetModuleFileNameW(nullptr, executable, 32768);
        CheckWin32(length != 0, "Cannot locate test executable.");
        Check(length < 32768, "Test executable path exceeds the buffer.");
        std::wstring command = L"\"" + std::wstring(executable) + L"\" \"" +
            fs::absolute(argv[1]).wstring() + L"\" --unelevated";
        STARTUPINFOW startup{sizeof(startup)};
        startup.dwFlags = STARTF_USESTDHANDLES;
        startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
        startup.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
        startup.hStdError = GetStdHandle(STD_ERROR_HANDLE);
        PROCESS_INFORMATION process{};
        std::cout << "Launching Shell tests with reduced privileges." << std::endl;
        if (!CreateProcessAsUserW(restricted.value, executable, command.data(), nullptr, nullptr, TRUE,
            0, nullptr, nullptr, &startup, &process))
            CheckHr(HRESULT_FROM_WIN32(GetLastError()), "Cannot launch non-administrator Shell tests.");
        Handle child{process.hProcess};
        Handle thread{process.hThread};
        CheckWin32(WaitForSingleObject(child.value, INFINITE) == WAIT_OBJECT_0, "Cannot wait for Shell tests.");
        DWORD result = 1;
        CheckWin32(GetExitCodeProcess(child.value, &result), "Cannot read Shell test result.");
        std::cout << "Shell test child exit code=" << result << " (" << Hex(result) << ")\n";
        return static_cast<int>(result);
    }

    struct RegistryFixture
    {
        std::wstring path;
        HKEY key = nullptr;
        explicit RegistryFixture(std::wstring value) : path(std::move(value))
        {
            DWORD disposition;
            CheckRegistry(RegCreateKeyExW(HKEY_CURRENT_USER, path.c_str(), 0, nullptr, 0,
                KEY_ALL_ACCESS, nullptr, &key, &disposition), "Cannot create test registry key.");
            if (disposition != REG_CREATED_NEW_KEY)
            {
                RegCloseKey(key);
                key = nullptr;
                throw std::runtime_error("Test registry key already exists; quit ClipDiff before running native tests.");
            }
        }
        ~RegistryFixture()
        {
            if (key) { RegCloseKey(key); RegDeleteTreeW(HKEY_CURRENT_USER, path.c_str()); }
        }
        void Set(const wchar_t* subkey, const wchar_t* name, const std::wstring& value)
        {
            HKEY child;
            CheckRegistry(RegCreateKeyExW(key, subkey, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &child, nullptr),
                "Cannot create registry value container.");
            const auto result = RegSetValueExW(child, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()),
                static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t)));
            RegCloseKey(child);
            CheckRegistry(result, "Cannot write test registry value.");
        }
    };

    struct Menu
    {
        HMENU handle = CreatePopupMenu();
        Menu() { CheckWin32(handle != nullptr, "CreatePopupMenu failed."); }
        ~Menu() { if (handle) DestroyMenu(handle); }
        UINT ClipDiffId() const
        {
            const int count = GetMenuItemCount(handle);
            CheckWin32(count != -1, "GetMenuItemCount failed.");
            for (int index = 0; index < count; ++index)
            {
                wchar_t label[256]{};
                MENUITEMINFOW item{sizeof(item)};
                item.fMask = MIIM_ID | MIIM_STRING;
                item.dwTypeData = label;
                item.cch = static_cast<UINT>(std::size(label));
                CheckWin32(GetMenuItemInfoW(handle, static_cast<UINT>(index), TRUE, &item), "GetMenuItemInfo failed.");
                if (std::wstring(label) == ClipDiffMenuLabel) return item.wID;
            }
            return 0;
        }
    };

    void DumpMenu(HMENU menu, unsigned depth = 0)
    {
        const int count = GetMenuItemCount(menu);
        const DWORD error = count == -1 ? GetLastError() : ERROR_SUCCESS;
        const std::string indent(depth * 2 + 2, ' ');
        std::cout << indent << "Menu item count=" << count;
        if (count == -1) std::cout << ", Win32=" << WindowsError(error);
        std::cout << '\n';
        for (int index = 0; index < count; ++index)
        {
            wchar_t label[512]{};
            MENUITEMINFOW item{sizeof(item)};
            item.fMask = MIIM_ID | MIIM_STRING | MIIM_STATE | MIIM_FTYPE | MIIM_SUBMENU;
            item.dwTypeData = label;
            item.cch = static_cast<UINT>(std::size(label));
            if (!GetMenuItemInfoW(menu, static_cast<UINT>(index), TRUE, &item))
            {
                const DWORD itemError = GetLastError();
                std::cout << indent << "Item[" << index << "] unreadable: " << WindowsError(itemError) << '\n';
                continue;
            }
            // ASCII escapes keep Unicode/control characters readable even in redirected PowerShell output.
            std::ostringstream text;
            for (const wchar_t character : std::wstring(label))
            {
                if (character >= 32 && character < 127) text << static_cast<char>(character);
                else text << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<unsigned>(character);
            }
            std::cout << indent << "Item[" << index << "] id=" << item.wID << ", type=" << Hex(item.fType)
                << ((item.fType & MFT_SEPARATOR) ? " (separator)" : "") << ", state=" << Hex(item.fState)
                << ", label=" << std::quoted(text.str()) << '\n';
            if (item.hSubMenu && depth < 8) DumpMenu(item.hSubMenu, depth + 1);
        }
    }

    UINT QueryMenu(IContextMenu* context, Menu& menu, const char* source, UINT flags = CMF_NORMAL,
        UINT firstId = 1, UINT lastId = 0x7FFF)
    {
        const HRESULT result = context->QueryContextMenu(menu.handle, 0, firstId, lastId, flags);
        std::cout << "  " << source << " QueryContextMenu: flags=" << Hex(flags) << ", ids=" << firstId << ".." << lastId
            << ", HRESULT=" << Hex(static_cast<DWORD>(result));
        if (SUCCEEDED(result)) std::cout << ", command IDs reserved=" << HRESULT_CODE(result);
        std::cout << '\n';
        CheckHr(result, source);
        const UINT id = menu.ClipDiffId();
        std::cout << "  " << source << ": visible=" << (id != 0) << ", ClipDiff ID=" << id
            << ", top-level items=" << GetMenuItemCount(menu.handle) << '\n';
        return id;
    }

    class Selection
    {
        PIDLIST_ABSOLUTE parentId_ = nullptr;
        ComPtr<IShellFolder> folder_;
        std::vector<PCUITEMID_CHILD> children_;
    public:
        Selection(const fs::path& parent, const std::vector<std::wstring>& names)
        {
            CheckHr(SHParseDisplayName(parent.c_str(), nullptr, &parentId_, 0, nullptr), "Cannot parse test directory.");
            ComPtr<IShellFolder> desktop;
            CheckHr(SHGetDesktopFolder(&desktop), "Cannot get desktop folder.");
            CheckHr(desktop->BindToObject(parentId_, nullptr, IID_PPV_ARGS(&folder_)), "Cannot bind test directory.");
            for (const auto& name : names)
            {
                PIDLIST_RELATIVE child = nullptr;
                CheckHr(folder_->ParseDisplayName(nullptr, nullptr, const_cast<wchar_t*>(name.c_str()), nullptr, &child, nullptr),
                    "Cannot parse test item.");
                children_.push_back(child);
            }
        }
        ~Selection()
        {
            for (const auto child : children_) CoTaskMemFree(const_cast<PUITEMID_CHILD>(child));
            CoTaskMemFree(parentId_);
        }
        ComPtr<IDataObject> Data()
        {
            ComPtr<IDataObject> data;
            CheckHr(folder_->GetUIObjectOf(nullptr, static_cast<UINT>(children_.size()), children_.data(),
                IID_IDataObject, nullptr, &data), "Cannot obtain real Shell selection data.");
            return data;
        }
        ComPtr<IContextMenu> ShellMenu()
        {
            DEFCONTEXTMENU definition{};
            definition.pidlFolder = parentId_;
            definition.psf = folder_.Get();
            definition.cidl = static_cast<UINT>(children_.size());
            definition.apidl = children_.data();
            // Resolve the real file associations, including the same wildcard handler used by ClipDiff.
            ComPtr<IContextMenu> menu;
            CheckHr(SHCreateDefaultContextMenu(&definition, IID_PPV_ARGS(&menu)), "Cannot create the Windows context menu.");
            return menu;
        }
    };

    class Receiver final : public IDropTarget
    {
        std::atomic<ULONG> references_{1};
    public:
        std::vector<std::wstring> received;
        int drops = 0;
        IFACEMETHODIMP QueryInterface(REFIID iid, void** result) override
        {
            *result = nullptr;
            if (iid != IID_IUnknown && iid != IID_IDropTarget) return E_NOINTERFACE;
            *result = static_cast<IDropTarget*>(this); AddRef(); return S_OK;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return ++references_; }
        IFACEMETHODIMP_(ULONG) Release() override { const auto count = --references_; if (!count) delete this; return count; }
        IFACEMETHODIMP DragEnter(IDataObject*, DWORD, POINTL, DWORD* effect) override { *effect = DROPEFFECT_COPY; return S_OK; }
        IFACEMETHODIMP DragOver(DWORD, POINTL, DWORD* effect) override { *effect = DROPEFFECT_COPY; return S_OK; }
        IFACEMETHODIMP DragLeave() override { return S_OK; }
        static HRESULT ReadPaths(IDataObject* data, std::vector<std::wstring>& paths)
        {
            FORMATETC format{CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
            STGMEDIUM medium{};
            const HRESULT result = data->GetData(&format, &medium);
            if (FAILED(result)) return result;
            paths.clear();
            const auto drop = static_cast<HDROP>(medium.hGlobal);
            const UINT count = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
            for (UINT index = 0; index < count; ++index)
            {
                const UINT length = DragQueryFileW(drop, index, nullptr, 0);
                std::wstring path(length + 1, L'\0');
                DragQueryFileW(drop, index, path.data(), length + 1);
                path.resize(length);
                paths.push_back(std::move(path));
            }
            ReleaseStgMedium(&medium);
            return S_OK;
        }
        IFACEMETHODIMP Drop(IDataObject* data, DWORD, POINTL, DWORD* effect) override
        {
            const HRESULT result = ReadPaths(data, received);
            if (FAILED(result)) return result;
            ++drops;
            *effect = DROPEFFECT_COPY;
            return S_OK;
        }
    };

    class ReceiverFactory final : public IClassFactory
    {
        std::atomic<ULONG> references_{1};
        ComPtr<Receiver> receiver_;
    public:
        explicit ReceiverFactory(Receiver* receiver) : receiver_(receiver) {}
        IFACEMETHODIMP QueryInterface(REFIID iid, void** result) override
        {
            *result = nullptr;
            if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
            *result = static_cast<IClassFactory*>(this); AddRef(); return S_OK;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return ++references_; }
        IFACEMETHODIMP_(ULONG) Release() override { const auto count = --references_; if (!count) delete this; return count; }
        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID iid, void** result) override
        {
            if (outer) return CLASS_E_NOAGGREGATION;
            return receiver_->QueryInterface(iid, result);
        }
        IFACEMETHODIMP LockServer(BOOL) override { return S_OK; }
    };

    ComPtr<IShellExtInit> CreateHandler(IClassFactory* factory, IDataObject* selection)
    {
        ComPtr<IShellExtInit> handler;
        CheckHr(factory->CreateInstance(nullptr, IID_PPV_ARGS(&handler)), "Cannot create handler.");
        CheckHr(handler->Initialize(nullptr, selection, nullptr), "Cannot initialize handler.");
        return handler;
    }

    void Verify(const fs::path& directory, IClassFactory* factory, HANDLE ready)
    {
        struct Case { const char* label; std::vector<std::wstring> names; bool visible; };
        const std::vector<Case> cases{
            {"one file", {L"old file.txt"}, false},
            {"two files, mixed .txt/.cmd types and Unicode name", {L"old file.txt", L"new 雪.cmd"}, true},
            {"three files", {L"old file.txt", L"new 雪.cmd", L"third.txt"}, false},
            {"file then folder", {L"old file.txt", L"folder"}, false},
            {"folder then file", {L"folder", L"new 雪.cmd"}, false}
        };
        for (const auto& test : cases)
        {
            BeginCase(test.label);
            std::cout << "  Selection count=" << test.names.size() << ", expected visible=" << test.visible << '\n';
            Selection selection(directory, test.names);
            auto data = selection.Data();
            PrintSelection(data.Get(), ready);
            auto handler = CreateHandler(factory, data.Get());
            ComPtr<IContextMenu> directContext;
            CheckHr(handler.As(&directContext), "Missing direct menu interface.");
            Menu directMenu;
            const bool directVisible = QueryMenu(directContext.Get(), directMenu, "Direct handler") != 0;
            if (directVisible != test.visible) DumpMenu(directMenu.handle);
            Check(directVisible == test.visible, "Direct handler visibility differs from the expectation above.");
            // Let Windows discover and initialize the DLL, rather than calling a made-up state API.
            auto shellMenu = selection.ShellMenu();
            Menu menu;
            const bool shellVisible = QueryMenu(shellMenu.Get(), menu, "Windows aggregate") != 0;
            if (shellVisible != test.visible)
            {
                DumpMenu(menu.handle);
                PrintShellRestrictions();
                throw std::runtime_error("Windows aggregate visibility mismatch: expected=" + std::to_string(test.visible) +
                    ", actual=" + std::to_string(shellVisible) + ", direct=" + std::to_string(directVisible) +
                    ". DLL loading, registered COM activation and the direct selection check passed. "
                    "Investigate Shell discovery/aggregation and the registration/restriction snapshot above.");
            }
            std::cout << "[PASS] " << test.label << '\n';
        }

        BeginCase("empty/background selection");
        auto empty = CreateHandler(factory, nullptr);
        ComPtr<IContextMenu> emptyMenu;
        CheckHr(empty.As(&emptyMenu), "Missing IContextMenu.");
        Menu noSelection;
        Check(!QueryMenu(emptyMenu.Get(), noSelection, "Empty selection", CMF_NORMAL, 1, 10),
            "Background selection must be hidden.");

        BeginCase("pair fixture identity and order");
        Selection pair(directory, {L"old file.txt", L"new 雪.cmd"});
        auto data = pair.Data();
        // The Shell can expand short directory names in GetTempPath's result. Verify fixture identity
        // and then compare delivery with the exact paths/order supplied by the real Shell data object.
        std::vector<std::wstring> suppliedPaths;
        CheckHr(Receiver::ReadPaths(data.Get(), suppliedPaths), "Cannot read Shell fixture paths.");
        Check(suppliedPaths.size() == 2 && fs::equivalent(suppliedPaths[0], directory / L"old file.txt") &&
            fs::equivalent(suppliedPaths[1], directory / L"new 雪.cmd"), "Shell fixture order or identity is wrong.");
        auto handler = CreateHandler(factory, data.Get());
        ComPtr<IContextMenu> context;
        CheckHr(handler.As(&context), "Missing menu interface.");
        BeginCase("default-action exclusion");
        Menu defaultMenu;
        Check(!QueryMenu(context.Get(), defaultMenu, "Default action", CMF_DEFAULTONLY, 1, 10),
            "Default action must not gain a ClipDiff command.");

        BeginCase("pause and cached invocation rejection");
        CheckWin32(ResetEvent(ready), "Cannot reset readiness event for pause.");
        Menu paused;
        Check(!QueryMenu(context.Get(), paused, "Paused handler", CMF_NORMAL, 1, 10),
            "Existing handler must hide while monitoring is paused.");
        CMINVOKECOMMANDINFO command{sizeof(command)};
        Check(FAILED(context->InvokeCommand(&command)), "Cached invocation must fail while paused.");
        BeginCase("resume, command ID allocation and canonical verb");
        CheckWin32(SetEvent(ready), "Cannot set readiness event for resume.");
        CheckHr(handler->Initialize(nullptr, data.Get(), nullptr), "Resume initialization failed.");
        Menu resumed;
        Check(QueryMenu(context.Get(), resumed, "Resumed handler", CMF_NORMAL, 17, 99) == 17,
            "Menu identifier allocation failed: expected ClipDiff ID=17.");
        wchar_t verb[64]{};
        CheckHr(context->GetCommandString(0, GCS_VERBW, nullptr, reinterpret_cast<LPSTR>(verb), 64), "Cannot get verb.");
        Check(std::wstring(verb) == ClipDiffVerb, "Unexpected canonical verb.");

        BeginCase("COM drop receiver registration");
        ComPtr<Receiver> receiver;
        receiver.Attach(new Receiver());
        ComPtr<ReceiverFactory> receiverFactory;
        receiverFactory.Attach(new ReceiverFactory(receiver.Get()));
        DWORD cookie;
        CheckHr(CoRegisterClassObject(CLSID_ClipDiffDropTarget, receiverFactory.Get(), CLSCTX_LOCAL_SERVER,
            REGCLS_MULTIPLEUSE, &cookie), "Cannot register test drop receiver.");
        try
        {
            BeginCase("Windows aggregate invocation and ordered Unicode delivery");
            // Invoke through Windows' aggregate menu, exercising handler discovery and command offset translation.
            auto shellMenu = pair.ShellMenu();
            Menu menu;
            const UINT id = QueryMenu(shellMenu.Get(), menu, "Windows invocation menu");
            if (!id) { DumpMenu(menu.handle); PrintShellRestrictions(); }
            Check(id != 0, "The real Shell menu did not offer ClipDiff for two files.");
            command.lpVerb = MAKEINTRESOURCEA(id - 1);
            CheckHr(shellMenu->InvokeCommand(&command), "Shell invocation did not reach the drop receiver.");
            Check(receiver->drops == 1, "Expected one atomic pair delivery.");
            Check(receiver->received == suppliedPaths, "Selection order or Unicode paths changed in transit.");

            // Both canonical Unicode verbs and numeric offsets are part of the native contract.
            BeginCase("Unicode verb, unknown verb rejection and selection release");
            CMINVOKECOMMANDINFOEX unicode{sizeof(unicode)};
            unicode.fMask = CMIC_MASK_UNICODE;
            unicode.lpVerbW = L"Not.ClipDiff";
            Check(FAILED(context->InvokeCommand(reinterpret_cast<CMINVOKECOMMANDINFO*>(&unicode))), "Unknown verb accepted.");
            unicode.lpVerbW = ClipDiffVerb;
            CheckHr(context->InvokeCommand(reinterpret_cast<CMINVOKECOMMANDINFO*>(&unicode)), "Unicode verb failed.");
            Check(receiver->drops == 2, "Unicode verb was not delivered.");
            Check(FAILED(context->InvokeCommand(reinterpret_cast<CMINVOKECOMMANDINFO*>(&unicode))), "Selection retained after invocation.");
        }
        catch (...) { CoRevokeClassObject(cookie); throw; }
        CoRevokeClassObject(cookie);
    }
}

int wmain(int argc, wchar_t** argv)
{
    // Flush each diagnostic immediately, including when an elevated parent inherits redirected output.
    std::cout << std::unitbuf;
    if (argc != 2 && !(argc == 3 && std::wstring(argv[2]) == L"--unelevated"))
    { std::cerr << "Supply the native extension DLL path.\n"; return 1; }
    try
    {
        BeginCase("process elevation");
        if (const auto result = RunWithoutElevation(argc, argv)) return *result;
        std::cout << "Running native Shell tests without elevation.\n";
    }
    catch (const std::exception& error) { std::cerr << "[FAIL] " << currentCase << ": " << error.what() << '\n'; return 1; }
    const HRESULT initialized = OleInitialize(nullptr);
    if (FAILED(initialized))
    {
        std::cerr << "[FAIL] OleInitialize: HRESULT=" << WindowsError(static_cast<DWORD>(initialized)) << '\n';
        return 1;
    }
    HANDLE ready = nullptr;
    HMODULE library = nullptr;
    fs::path directory;
    bool ownsDirectory = false;
    int exitCode = 1;
    try
    {
        BeginCase("readiness event setup");
        ready = CreateEventW(nullptr, TRUE, FALSE, ClipDiffReadyEvent);
        const DWORD eventError = GetLastError();
        CheckWin32(ready != nullptr, "Cannot create readiness event.");
        Check(eventError != ERROR_ALREADY_EXISTS, "Quit ClipDiff before running native tests.");
        BeginCase("DLL loading and exported class factory");
        const fs::path dll = fs::absolute(argv[1]);
        library = LoadLibraryW(dll.c_str());
        CheckWin32(library != nullptr, "Cannot load extension DLL.");
        const auto getFactory = reinterpret_cast<HRESULT(STDAPICALLTYPE*)(REFCLSID, REFIID, void**)>(GetProcAddress(library, "DllGetClassObject"));
        CheckWin32(getFactory != nullptr, "Missing class factory export.");
        ComPtr<IClassFactory> factory;
        CheckHr(getFactory(CLSID_ClipDiffShellExtension, IID_PPV_ARGS(&factory)), "Cannot get class factory.");

        BeginCase("per-user COM registration and Windows activation");
        RegistryFixture registration(L"Software\\Classes\\CLSID\\{6B46A974-40E2-4AD4-9F68-E534202B11E8}");
        registration.Set(L"InprocServer32", nullptr, dll.wstring());
        registration.Set(L"InprocServer32", L"ThreadingModel", L"Apartment");
        ComPtr<IShellExtInit> registeredHandler;
        CheckHr(CoCreateInstance(CLSID_ClipDiffShellExtension, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(&registeredHandler)), "Windows could not activate the per-user registered extension.");
        std::cout << "  Registered COM activation passed.\n";
        BeginCase("wildcard context-menu registration");
        RegistryFixture association(L"Software\\Classes\\*\\shellex\\ContextMenuHandlers\\ClipDiff.CompareSelected");
        association.Set(L"", nullptr, L"{6B46A974-40E2-4AD4-9F68-E534202B11E8}");
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
        std::cout << "Effective HKCR registration (test process registry view):\n";
        const auto serverKey = L"CLSID\\{6B46A974-40E2-4AD4-9F68-E534202B11E8}\\InprocServer32";
        PrintRegistryValue(HKEY_CLASSES_ROOT, "HKCR", serverKey, nullptr, "ClipDiff InprocServer32", false, dll.c_str());
        PrintRegistryValue(HKEY_CLASSES_ROOT, "HKCR", serverKey, L"ThreadingModel", "ClipDiff ThreadingModel", false, L"Apartment");
        PrintRegistryValue(HKEY_CLASSES_ROOT, "HKCR", L"*\\shellex\\ContextMenuHandlers\\ClipDiff.CompareSelected",
            nullptr, "wildcard ClipDiff handler CLSID", false, L"{6B46A974-40E2-4AD4-9F68-E534202B11E8}");
        BeginCase("synthetic file and folder fixtures");
        directory = fs::temp_directory_path() / (L"ClipDiff.ShellTests." + std::to_wstring(GetCurrentProcessId()));
        Check(fs::create_directory(directory), "Test directory already exists.");
        ownsDirectory = true;
        fs::create_directory(directory / L"folder");
        for (const auto name : {L"old file.txt", L"new 雪.cmd", L"third.txt"}) std::ofstream(directory / name) << "test fixture";
        CheckWin32(SetEvent(ready), "Cannot signal test readiness.");
        Verify(directory, factory.Get(), ready);
        BeginCase("cleanup");
        CheckWin32(ResetEvent(ready), "Cannot reset test readiness.");
        std::cout << "Native Shell menu discovery, selection visibility, pause/resume and COM delivery tests passed.\n";
        exitCode = 0;
    }
    catch (const std::exception& error) { std::cerr << "[FAIL] " << currentCase << ": " << error.what() << '\n'; }
    // Do not reset a readiness event belonging to a running ClipDiff instance after a refused test run.
    if (ready) CloseHandle(ready);
    if (library) FreeLibrary(library);
    if (ownsDirectory) { std::error_code ignored; fs::remove_all(directory, ignored); }
    OleUninitialize();
    return exitCode;
}
