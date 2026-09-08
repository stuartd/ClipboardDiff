#include "../../src/ClipDiff.ShellExtension/ShellExtension.h"
#include <wrl/client.h>
#include <atomic>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace fs = std::filesystem;

namespace
{
    void Check(bool condition, const char* message)
    {
        if (!condition) throw std::runtime_error(message);
    }
    void CheckHr(HRESULT result, const char* message)
    {
        if (FAILED(result))
        {
            std::cerr << "HRESULT: 0x" << std::hex << static_cast<unsigned long>(result) << std::dec << '\n';
            throw std::runtime_error(message);
        }
    }

    struct RegistryFixture
    {
        std::wstring path;
        HKEY key = nullptr;
        explicit RegistryFixture(std::wstring value) : path(std::move(value))
        {
            DWORD disposition;
            Check(RegCreateKeyExW(HKEY_CURRENT_USER, path.c_str(), 0, nullptr, 0,
                KEY_ALL_ACCESS, nullptr, &key, &disposition) == ERROR_SUCCESS, "Cannot create test registry key.");
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
            Check(RegCreateKeyExW(key, subkey, 0, nullptr, 0, KEY_SET_VALUE, nullptr, &child, nullptr) == ERROR_SUCCESS,
                "Cannot create registry value container.");
            const auto result = RegSetValueExW(child, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()),
                static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t)));
            RegCloseKey(child);
            Check(result == ERROR_SUCCESS, "Cannot write test registry value.");
        }
    };

    struct Menu
    {
        HMENU handle = CreatePopupMenu();
        ~Menu() { if (handle) DestroyMenu(handle); }
        UINT ClipDiffId() const
        {
            const int count = GetMenuItemCount(handle);
            for (int index = 0; index < count; ++index)
            {
                wchar_t label[256]{};
                GetMenuStringW(handle, static_cast<UINT>(index), label, 256, MF_BYPOSITION);
                if (std::wstring(label) == ClipDiffMenuLabel) return GetMenuItemID(handle, index);
            }
            return 0;
        }
    };

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
        ComPtr<IContextMenu> ShellMenu(HKEY association)
        {
            DEFCONTEXTMENU definition{};
            definition.pidlFolder = parentId_;
            definition.psf = folder_.Get();
            definition.cidl = static_cast<UINT>(children_.size());
            definition.apidl = children_.data();
            definition.cKeys = 1;
            definition.aKeys = &association;
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
        IFACEMETHODIMP Drop(IDataObject* data, DWORD, POINTL, DWORD* effect) override
        {
            FORMATETC format{CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
            STGMEDIUM medium{};
            const HRESULT result = data->GetData(&format, &medium);
            if (FAILED(result)) return result;
            received.clear();
            const auto drop = static_cast<HDROP>(medium.hGlobal);
            const UINT count = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
            for (UINT index = 0; index < count; ++index)
            {
                const UINT length = DragQueryFileW(drop, index, nullptr, 0);
                std::wstring path(length + 1, L'\0');
                DragQueryFileW(drop, index, path.data(), length + 1);
                path.resize(length);
                received.push_back(std::move(path));
            }
            ReleaseStgMedium(&medium);
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

    void Verify(const fs::path& directory, IClassFactory* factory, HANDLE ready, HKEY association)
    {
        struct Case { std::vector<std::wstring> names; bool visible; };
        const std::vector<Case> cases{
            {{L"old file.txt"}, false},
            {{L"old file.txt", L"new 雪.cmd"}, true},
            {{L"old file.txt", L"new 雪.cmd", L"third.txt"}, false},
            {{L"old file.txt", L"folder"}, false},
            {{L"folder", L"new 雪.cmd"}, false}
        };
        for (const auto& test : cases)
        {
            Selection selection(directory, test.names);
            auto data = selection.Data();
            auto handler = CreateHandler(factory, data.Get());
            ComPtr<IContextMenu> directContext;
            CheckHr(handler.As(&directContext), "Missing direct menu interface.");
            Menu directMenu;
            CheckHr(directContext->QueryContextMenu(directMenu.handle, 0, 1, 0x7FFF, CMF_NORMAL), "Direct menu query failed.");
            std::cout << "Selection count=" << test.names.size() << ", expected=" << test.visible
                << ", direct=" << (directMenu.ClipDiffId() != 0) << std::endl;
            Check((directMenu.ClipDiffId() != 0) == test.visible, "Handler rejected the real Shell selection.");
            // Let Windows discover and initialize the DLL, rather than calling a made-up state API.
            auto shellMenu = selection.ShellMenu(association);
            Menu menu;
            CheckHr(shellMenu->QueryContextMenu(menu.handle, 0, 1, 0x7FFF, CMF_NORMAL), "Windows menu query failed.");
            std::cout << "Shell aggregate visible=" << (menu.ClipDiffId() != 0) << std::endl;
            if ((menu.ClipDiffId() != 0) != test.visible)
            {
                for (int index = 0; index < GetMenuItemCount(menu.handle); ++index)
                {
                    wchar_t label[256]{};
                    GetMenuStringW(menu.handle, static_cast<UINT>(index), label, 256, MF_BYPOSITION);
                    std::wcout << L"Menu item: " << label << std::endl;
                }
            }
            Check((menu.ClipDiffId() != 0) == test.visible, "Windows supplied an unexpected menu visibility result.");
        }

        auto empty = CreateHandler(factory, nullptr);
        ComPtr<IContextMenu> emptyMenu;
        CheckHr(empty.As(&emptyMenu), "Missing IContextMenu.");
        Menu noSelection;
        CheckHr(emptyMenu->QueryContextMenu(noSelection.handle, 0, 1, 10, CMF_NORMAL), "Empty menu query failed.");
        Check(!noSelection.ClipDiffId(), "Background selection must be hidden.");

        Selection pair(directory, {L"old file.txt", L"new 雪.cmd"});
        auto data = pair.Data();
        auto handler = CreateHandler(factory, data.Get());
        ComPtr<IContextMenu> context;
        CheckHr(handler.As(&context), "Missing menu interface.");
        Menu defaultMenu;
        CheckHr(context->QueryContextMenu(defaultMenu.handle, 0, 1, 10, CMF_DEFAULTONLY), "Default menu query failed.");
        Check(!defaultMenu.ClipDiffId(), "Default action must not gain a ClipDiff command.");

        ResetEvent(ready);
        Menu paused;
        CheckHr(context->QueryContextMenu(paused.handle, 0, 1, 10, CMF_NORMAL), "Paused menu query failed.");
        Check(!paused.ClipDiffId(), "Existing handler must hide while monitoring is paused.");
        CMINVOKECOMMANDINFO command{sizeof(command)};
        Check(FAILED(context->InvokeCommand(&command)), "Cached invocation must fail while paused.");
        SetEvent(ready);
        CheckHr(handler->Initialize(nullptr, data.Get(), nullptr), "Resume initialization failed.");
        Menu resumed;
        CheckHr(context->QueryContextMenu(resumed.handle, 0, 17, 99, CMF_NORMAL), "Resumed query failed.");
        Check(resumed.ClipDiffId() == 17, "Menu identifier allocation failed.");
        wchar_t verb[64]{};
        CheckHr(context->GetCommandString(0, GCS_VERBW, nullptr, reinterpret_cast<LPSTR>(verb), 64), "Cannot get verb.");
        Check(std::wstring(verb) == ClipDiffVerb, "Unexpected canonical verb.");

        ComPtr<Receiver> receiver;
        receiver.Attach(new Receiver());
        ComPtr<ReceiverFactory> receiverFactory;
        receiverFactory.Attach(new ReceiverFactory(receiver.Get()));
        DWORD cookie;
        CheckHr(CoRegisterClassObject(CLSID_ClipDiffDropTarget, receiverFactory.Get(), CLSCTX_LOCAL_SERVER,
            REGCLS_MULTIPLEUSE, &cookie), "Cannot register test drop receiver.");
        try
        {
            // Invoke through Windows' aggregate menu, exercising handler discovery and command offset translation.
            auto shellMenu = pair.ShellMenu(association);
            Menu menu;
            CheckHr(shellMenu->QueryContextMenu(menu.handle, 0, 1, 0x7FFF, CMF_NORMAL), "Invoke menu query failed.");
            const UINT id = menu.ClipDiffId();
            Check(id != 0, "The real Shell menu did not offer ClipDiff for two files.");
            command.lpVerb = MAKEINTRESOURCEA(id - 1);
            CheckHr(shellMenu->InvokeCommand(&command), "Shell invocation did not reach the drop receiver.");
            Check(receiver->drops == 1, "Expected one atomic pair delivery.");
            Check(receiver->received == std::vector<std::wstring>{(directory / L"old file.txt").wstring(),
                (directory / L"new 雪.cmd").wstring()}, "Selection order or Unicode paths changed in transit.");

            // Both canonical Unicode verbs and numeric offsets are part of the native contract.
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
    if (argc != 2) { std::cerr << "Supply the native extension DLL path.\n"; return 1; }
    const HRESULT initialized = OleInitialize(nullptr);
    if (FAILED(initialized)) return 1;
    HANDLE ready = nullptr;
    HMODULE library = nullptr;
    fs::path directory;
    bool ownsDirectory = false;
    int exitCode = 1;
    try
    {
        ready = CreateEventW(nullptr, TRUE, FALSE, ClipDiffReadyEvent);
        Check(ready != nullptr, "Cannot create readiness event.");
        Check(GetLastError() != ERROR_ALREADY_EXISTS, "Quit ClipDiff before running native tests.");
        const fs::path dll = fs::absolute(argv[1]);
        library = LoadLibraryW(dll.c_str());
        Check(library != nullptr, "Cannot load extension DLL.");
        const auto getFactory = reinterpret_cast<HRESULT(STDAPICALLTYPE*)(REFCLSID, REFIID, void**)>(GetProcAddress(library, "DllGetClassObject"));
        Check(getFactory != nullptr, "Missing class factory export.");
        ComPtr<IClassFactory> factory;
        CheckHr(getFactory(CLSID_ClipDiffShellExtension, IID_PPV_ARGS(&factory)), "Cannot get class factory.");

        RegistryFixture registration(L"Software\\Classes\\CLSID\\{6B46A974-40E2-4AD4-9F68-E534202B11E8}");
        registration.Set(L"InprocServer32", nullptr, dll.wstring());
        registration.Set(L"InprocServer32", L"ThreadingModel", L"Apartment");
        ComPtr<IShellExtInit> registeredHandler;
        CheckHr(CoCreateInstance(CLSID_ClipDiffShellExtension, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(&registeredHandler)), "Windows could not activate the per-user registered extension.");
        RegistryFixture association(L"Software\\Classes\\ClipDiff.ShellTests." + std::to_wstring(GetCurrentProcessId()));
        association.Set(L"shellex\\ContextMenuHandlers\\ClipDiff", nullptr, L"{6B46A974-40E2-4AD4-9F68-E534202B11E8}");
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
        directory = fs::temp_directory_path() / (L"ClipDiff.ShellTests." + std::to_wstring(GetCurrentProcessId()));
        Check(fs::create_directory(directory), "Test directory already exists.");
        ownsDirectory = true;
        fs::create_directory(directory / L"folder");
        for (const auto name : {L"old file.txt", L"new 雪.cmd", L"third.txt"}) std::ofstream(directory / name) << "test fixture";
        SetEvent(ready);
        Verify(directory, factory.Get(), ready, association.key);
        ResetEvent(ready);
        std::cout << "Native Shell menu discovery, selection visibility, pause/resume and COM delivery tests passed.\n";
        exitCode = 0;
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; }
    // Do not reset a readiness event belonging to a running ClipDiff instance after a refused test run.
    if (ready) CloseHandle(ready);
    if (library) FreeLibrary(library);
    if (ownsDirectory) { std::error_code ignored; fs::remove_all(directory, ignored); }
    OleUninitialize();
    return exitCode;
}
