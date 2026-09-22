; TRWhisper kurulum betiği (Inno Setup 6.4+)
; Derleme: scripts\build-installer.ps1  (elle ISCC çağırmayın; önbellek dosyaları gerekir)

#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif

#define AppName "TRWhisper"
#define AppPublisher "Mustafa Kağan Güngör"
#define AppURL "https://github.com/kagangungor/TRWhisper"
#define AppExe "TRWhisper.exe"

; whisper.cpp sürümü sabittir: "latest" bazı sürümlerde indirilebilir paket içermiyor (404).
#define WhisperBuild "b4938"
#define CudaZipName "whisper-cublas-12.4.0-bin-x64.zip"
#define CudaZipUrl "https://github.com/ggml-org/whisper.cpp/releases/download/b4938/whisper-cublas-12.4.0-bin-x64.zip"
#define CudaZipSha "c1b17166e1e31a91cc8e9c1f910d3785e3ce757bb2958bf9dce13fdb4880005f"
#define CudaZipSize 671045732
#define CudaDllSize 537670144

; Hugging Face adresleri commit'e sabitlenmiştir (main değişirse SHA-256 tutmazdı).
#define TurboName "ggml-large-v3-turbo-q5_0.bin"
#define TurboUrl "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo-q5_0.bin"
#define TurboSha "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2"
#define TurboSize 574041195

#define SmallName "ggml-small.bin"
#define SmallUrl "https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-small.bin"
#define SmallSha "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"
#define SmallSize 487601967

#define VcRedistUrl "https://aka.ms/vs/17/release/vc_redist.x64.exe"
#define VcRedistSize 25000000

; NVIDIA CUDA 12.4 için en düşük sürücü sürümü (Windows): 551.61
#define MinNvidiaDriver "551.61"

[Setup]
AppId={{0A9671E7-5707-4BBF-96D9-5D8DB4E37BF4}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; Uygulama config.json'u kendi klasörüne yazdığı için kullanıcı profiline kurulur (yönetici gerekmez).
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=Output
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=4
WizardStyle=modern
SetupIconFile=..\assets\TRWhisper.ico
ArchiveExtraction=full
LicenseFile=..\LICENSE
; Uygulama çalışıyorsa kullanıcıdan kapatması istenir (Program.cs'deki mutex adı).
AppMutex=Global\TRWhisper_SingleInstance_Mutex
CloseApplications=yes
SetupLogging=yes
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"; LicenseFile: "i18n\license-tr.txt"; InfoBeforeFile: "i18n\info-tr.txt"
Name: "en"; MessagesFile: "compiler:Default.isl"; InfoBeforeFile: "i18n\info-en.txt"

[CustomMessages]
tr.EnginePageCaption=Ses tanıma motoru
tr.EnginePageDesc=TRWhisper konuşmanızı hangi donanımla metne çevirsin?
tr.EnginePageSub=Kurulumdan sonra bu seçimi değiştirmek için kurulumu tekrar çalıştırmanız yeterlidir.
tr.EngineCpu=CPU sürümü — her bilgisayarda çalışır (kuruluma dahil, ~12 MB)
tr.EngineCuda=NVIDIA GPU sürümü (CUDA 12.4) — çok daha hızlı (~640 MB indirme, diskte ~1,2 GB)
tr.GpuFound=Bilgisayarınızda NVIDIA ekran kartı bulundu (sürücü %1). GPU sürümü önerilir: Large-v3 Turbo modeli ~23 saniye yerine ~3 saniyede çözümlenir.
tr.GpuOldDriver=NVIDIA ekran kartı bulundu ama sürücü sürümü (%1) CUDA 12.4 için eski. En az {#MinNvidiaDriver} gerekir; sürücünüzü güncellemeniz önerilir.
tr.GpuNotFound=Bilgisayarınızda CUDA destekli NVIDIA ekran kartı bulunamadı. CPU sürümü önerilir.
tr.GpuConfirm=Bilgisayarınızda CUDA destekli bir NVIDIA ekran kartı bulunamadı. GPU sürümü yine de kurulabilir (ekran kartı yoksa otomatik olarak CPU'ya döner) ama ~640 MB fazladan indirilir.%n%nGPU sürümüyle devam etmek istiyor musunuz?
tr.ModelPageCaption=Konuşma modelleri
tr.ModelPageDesc=Hangi Whisper modelleri kurulsun? (en az bir tanesi)
tr.ModelPageSub=Birden fazla model kurarsanız sistem tepsisindeki menüden istediğiniz an geçiş yapabilirsiniz.
tr.ModelTurbo=Large-v3 Turbo — en doğru sonuç, GPU önerilir (~547 MB indirme)
tr.ModelSmall=Small — CPU'da hızlı, doğruluğu daha düşük (~465 MB indirme)
tr.ModelInstalled=%1 (zaten kurulu, yeniden indirilmez)
tr.ModelNoneSelected=En az bir konuşma modeli seçmelisiniz. Model olmadan TRWhisper konuşmayı metne çeviremez.
tr.ModelTurboOnCpu=CPU sürümüyle Large-v3 Turbo modeli oldukça yavaştır (kısa bir dikte ~23 saniye). Small modeli CPU için daha uygundur.%n%nYine de Turbo ile devam edilsin mi?
tr.TaskStartMenu=Başlat menüsü kısayolu oluştur
tr.TaskAutostart=Windows açıldığında TRWhisper'ı otomatik başlat
tr.RunMicSettings=Windows mikrofon izinleri ayarlarını aç
tr.MemoEngine=Ses tanıma motoru:
tr.MemoModels=Konuşma modelleri:
tr.MemoDownload=İnternetten indirilecek:
tr.MemoDownloadNone=Yok (her şey bu kurulum dosyasında mevcut)
tr.MemoVcRedist=Microsoft Visual C++ 2015-2022 Runtime (x64) — eksik, kurulacak
tr.MemoAlways=Her zaman kurulur:
tr.MemoAlwaysItems=TRWhisper uygulaması (.NET 9 gömülü) + Silero VAD sessizlik modeli
tr.StatusExtractCuda=NVIDIA GPU dosyaları açılıyor (~1,2 GB, biraz sürebilir)...
tr.StatusVcRedist=Microsoft Visual C++ 2015-2022 Runtime kuruluyor...
tr.ExtractFailed=NVIDIA GPU paketi açılamadı: %1%n%nTRWhisper CPU sürümüyle kurulmaya devam edilecek.
tr.VcRedistPrompt=TRWhisper'ın ses tanıma motoru için "Microsoft Visual C++ 2015-2022 Runtime (x64)" gereklidir ve bilgisayarınızda bulunamadı.%n%nŞimdi kurulacak. Windows bir kez yönetici izni (UAC) soracak.
tr.VcRedistFailed=Microsoft Visual C++ Runtime kurulamadı. TRWhisper kuruldu ama ses tanıma ÇALIŞMAYACAK.%n%nLütfen şu adresten indirip kurun:%n{#VcRedistUrl}
tr.UninstallKeepData=Dikte geçmişiniz ve günlük dosyalarınız burada saklanıyor:%n%1%n%nBu klasör de silinsin mi?%n%n(Hayır derseniz dikte kayıtlarınız korunur.)
tr.NoDiskSpace=Kurulum için yeterli disk alanı yok. Gerekli: %1 MB, boş alan: %2 MB.

en.EnginePageCaption=Speech engine
en.EnginePageDesc=Which hardware should TRWhisper use for transcription?
en.EnginePageSub=To change this later, simply run this setup again.
en.EngineCpu=CPU build — works on every PC (included in this setup, ~12 MB)
en.EngineCuda=NVIDIA GPU build (CUDA 12.4) — much faster (~640 MB download, ~1.2 GB on disk)
en.GpuFound=An NVIDIA GPU was detected (driver %1). The GPU build is recommended: the Large-v3 Turbo model takes ~3 seconds instead of ~23 seconds.
en.GpuOldDriver=An NVIDIA GPU was detected, but its driver (%1) is too old for CUDA 12.4. At least {#MinNvidiaDriver} is required; please update your driver.
en.GpuNotFound=No CUDA-capable NVIDIA GPU was detected. The CPU build is recommended.
en.GpuConfirm=No CUDA-capable NVIDIA GPU was detected. The GPU build can still be installed (it falls back to the CPU automatically), but it downloads ~640 MB extra.%n%nContinue with the GPU build?
en.ModelPageCaption=Speech models
en.ModelPageDesc=Which Whisper models should be installed? (at least one)
en.ModelPageSub=If you install more than one, you can switch between them any time from the system tray menu.
en.ModelTurbo=Large-v3 Turbo — most accurate, GPU recommended (~547 MB download)
en.ModelSmall=Small — fast on CPU, less accurate (~465 MB download)
en.ModelInstalled=%1 (already installed, will not be downloaded again)
en.ModelNoneSelected=You must select at least one speech model. Without a model TRWhisper cannot transcribe speech.
en.ModelTurboOnCpu=On the CPU build the Large-v3 Turbo model is quite slow (~23 seconds for a short dictation). The Small model suits the CPU better.%n%nContinue with Turbo anyway?
en.TaskStartMenu=Create a Start menu shortcut
en.TaskAutostart=Start TRWhisper when Windows starts
en.RunMicSettings=Open Windows microphone privacy settings
en.MemoEngine=Speech engine:
en.MemoModels=Speech models:
en.MemoDownload=Will be downloaded:
en.MemoDownloadNone=Nothing (everything is inside this setup file)
en.MemoVcRedist=Microsoft Visual C++ 2015-2022 Runtime (x64) — missing, will be installed
en.MemoAlways=Always installed:
en.MemoAlwaysItems=TRWhisper application (.NET 9 embedded) + Silero VAD silence model
en.StatusExtractCuda=Extracting NVIDIA GPU files (~1.2 GB, this may take a while)...
en.StatusVcRedist=Installing Microsoft Visual C++ 2015-2022 Runtime...
en.ExtractFailed=The NVIDIA GPU package could not be extracted: %1%n%nSetup will continue with the CPU build.
en.VcRedistPrompt=TRWhisper's speech engine requires the "Microsoft Visual C++ 2015-2022 Runtime (x64)", which was not found on your PC.%n%nIt will be installed now. Windows will ask for administrator permission (UAC) once.
en.VcRedistFailed=The Microsoft Visual C++ Runtime could not be installed. TRWhisper is installed but speech recognition WILL NOT WORK.%n%nPlease download and install it from:%n{#VcRedistUrl}
en.UninstallKeepData=Your dictation history and log files are stored here:%n%1%n%nDelete this folder as well?%n%n(Choose No to keep your dictation records.)
en.NoDiskSpace=Not enough disk space. Required: %1 MB, available: %2 MB.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"
Name: "startmenuicon"; Description: "{cm:TaskStartMenu}"
Name: "autostart"; Description: "{cm:TaskAutostart}"; Flags: unchecked

[Files]
; --- Her zaman kurulanlar ---
Source: "..\publish\TRWhisper.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "i18n\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "cache\ggml-silero-v6.2.0.bin"; DestDir: "{app}\tools\whisper"; Flags: ignoreversion
; Kullanıcının ayarları yükseltmede korunur.
Source: "..\src\TRWhisper\config.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsremovereadonly

; --- CPU motoru (kuruluma gömülü) ---
Source: "cache\cpu\Release\whisper-cli.exe"; DestDir: "{app}\tools\whisper"; Flags: ignoreversion; Check: UseCpuEngine
Source: "cache\cpu\Release\whisper.dll"; DestDir: "{app}\tools\whisper"; Flags: ignoreversion; Check: UseCpuEngine
Source: "cache\cpu\Release\ggml.dll"; DestDir: "{app}\tools\whisper"; Flags: ignoreversion; Check: UseCpuEngine
Source: "cache\cpu\Release\ggml-base.dll"; DestDir: "{app}\tools\whisper"; Flags: ignoreversion; Check: UseCpuEngine
Source: "cache\cpu\Release\ggml-cpu-*.dll"; DestDir: "{app}\tools\whisper"; Flags: ignoreversion; Check: UseCpuEngine

; --- İndirilen modeller ---
Source: "{tmp}\{#TurboName}"; DestDir: "{app}\tools\whisper"; Flags: external ignoreversion skipifsourcedoesntexist; Check: WantTurbo
Source: "{tmp}\{#SmallName}"; DestDir: "{app}\tools\whisper"; Flags: external ignoreversion skipifsourcedoesntexist; Check: WantSmall

[InstallDelete]
; CPU motoruna geçildiyse CUDA dosyaları kaldırılır (yoksa GPU sanılır).
Type: files; Name: "{app}\tools\whisper\ggml-cuda.dll"; Check: UseCpuEngine
Type: files; Name: "{app}\tools\whisper\cublas64_12.dll"; Check: UseCpuEngine
Type: files; Name: "{app}\tools\whisper\cublasLt64_12.dll"; Check: UseCpuEngine
Type: files; Name: "{app}\tools\whisper\cudart64_12.dll"; Check: UseCpuEngine
; Eski (Kurulum.bat ile yapılmış) kurulum artıkları
Type: files; Name: "{app}\Kaldir.bat"
Type: files; Name: "{userstartup}\TRWhisper.lnk"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TRWhisper"; \
  ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "TRWhisper"; \
  Flags: deletevalue uninsdeletevalue; Tasks: not autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; WorkingDir: "{app}"; \
  Flags: nowait postinstall skipifsilent
Filename: "ms-settings:privacy-microphone"; Description: "{cm:RunMicSettings}"; \
  Flags: shellexec postinstall skipifsilent unchecked

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden skipifdoesntexist; RunOnceId: "KillTRWhisper"

[UninstallDelete]
; İndirilerek gelen dosyalar [Files] kaydında olmadığı için elle silinir.
Type: filesandordirs; Name: "{app}\tools"
Type: files; Name: "{app}\config.json"
Type: dirifempty; Name: "{app}"

[Code]
var
  EnginePage: TInputOptionWizardPage;
  ModelPage: TInputOptionWizardPage;
  DownloadPage: TDownloadWizardPage;
  GpuState: Integer;      // 0 = yok, 1 = var (sürücü eski), 2 = var (uygun)
  GpuDriver: String;
  NeedVcRedist: Boolean;
  ModelPageVisited: Boolean;
  DownloadBytes: Int64;
  CudaDownloaded: Boolean;

const
  ENGINE_CPU = 0;
  ENGINE_CUDA = 1;
  MODEL_TURBO = 0;
  MODEL_SMALL = 1;

function ToolsDir: String;
begin
  Result := ExpandConstant('{app}\tools\whisper');
end;

function FileHasSize(const FileName: String; const ExpectedSize: Int64): Boolean;
var
  Size: Int64;
begin
  Result := FileExists(FileName) and FileSize64(FileName, Size) and (Size = ExpectedSize);
end;

{ ---------- Donanım / bağımlılık tespiti ---------- }

// NVIDIA sürücü sürümü: dosya sürümü "32.0.15.6164" -> "561.64"
function NvidiaDriverVersion(const FileVersion: String): String;
var
  Parts: TArrayOfString;
  Digits: String;
begin
  Result := '';
  Parts := StringSplitEx(FileVersion, ['.'], #0, stExcludeEmpty);
  if GetArrayLength(Parts) < 4 then
    Exit;
  Digits := Parts[2] + Parts[3];
  if Length(Digits) < 5 then
    Exit;
  Digits := Copy(Digits, Length(Digits) - 4, 5);
  Result := Copy(Digits, 1, 3) + '.' + Copy(Digits, 4, 2);
end;

procedure SplitVersion(const V: String; var Major, Minor: Integer);
var
  P: Integer;
begin
  P := Pos('.', V);
  if P > 0 then
  begin
    Major := StrToIntDef(Copy(V, 1, P - 1), 0);
    Minor := StrToIntDef(Copy(V, P + 1, 10), 0);
  end
  else
  begin
    Major := StrToIntDef(V, 0);
    Minor := 0;
  end;
end;

// Sürücü sürümlerini karşılaştırır: -1 = A eski, 0 = eşit, 1 = A yeni
function CompareVersionStr(const A, B: String): Integer;
var
  AMajor, AMinor, BMajor, BMinor: Integer;
begin
  SplitVersion(A, AMajor, AMinor);
  SplitVersion(B, BMajor, BMinor);
  Result := 0;
  if AMajor < BMajor then Result := -1
  else if AMajor > BMajor then Result := 1
  else if AMinor < BMinor then Result := -1
  else if AMinor > BMinor then Result := 1;
end;

procedure DetectGpu;
var
  NvCuda, FileVer: String;
begin
  GpuState := 0;
  GpuDriver := '';
  NvCuda := ExpandConstant('{sys}\nvcuda.dll');
  if not FileExists(NvCuda) then
    Exit;
  if GetVersionNumbersString(NvCuda, FileVer) then
    GpuDriver := NvidiaDriverVersion(FileVer);
  if (GpuDriver <> '') and (CompareVersionStr(GpuDriver, '{#MinNvidiaDriver}') >= 0) then
    GpuState := 2
  else
    GpuState := 1;
  Log('TRWhisper: nvcuda.dll bulundu, surucu=' + GpuDriver + ' durum=' + IntToStr(GpuState));
end;

// whisper.cpp ikilileri MSVCP140/VCRUNTIME140/VCRUNTIME140_1/VCOMP140 olmadan çalışmaz.
function VcRuntimeMissing: Boolean;
var
  Sys: String;
begin
  Sys := ExpandConstant('{sys}\');
  Result := not (FileExists(Sys + 'msvcp140.dll') and FileExists(Sys + 'vcruntime140.dll') and
                 FileExists(Sys + 'vcruntime140_1.dll') and FileExists(Sys + 'vcomp140.dll'));
end;

{ ---------- Seçim yardımcıları ([Files] Check: ile kullanılır) ---------- }

function UseCpuEngine: Boolean;
begin
  Result := EnginePage.SelectedValueIndex = ENGINE_CPU;
end;

function UseCudaEngine: Boolean;
begin
  Result := EnginePage.SelectedValueIndex = ENGINE_CUDA;
end;

function WantTurbo: Boolean;
begin
  Result := ModelPage.Values[MODEL_TURBO];
end;

function WantSmall: Boolean;
begin
  Result := ModelPage.Values[MODEL_SMALL];
end;

function TurboInstalled: Boolean;
begin
  Result := FileHasSize(ToolsDir + '\{#TurboName}', {#TurboSize});
end;

function SmallInstalled: Boolean;
begin
  Result := FileHasSize(ToolsDir + '\{#SmallName}', {#SmallSize});
end;

function CudaInstalled: Boolean;
begin
  Result := FileHasSize(ToolsDir + '\ggml-cuda.dll', {#CudaDllSize});
end;

{ ---------- Sihirbaz ---------- }

// Sessiz kurulumda kullanıcıya soramayacağımız için çalışan örnek kapatılır
// (etkileşimli kurulumda AppMutex zaten kullanıcıdan kapatmasını ister).
function InitializeSetup: Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if WizardSilent then
    Exec(ExpandConstant('{sys}') + '\taskkill.exe', '/F /IM TRWhisper.exe', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Sessiz kurulum için:  /ENGINE=cpu|cuda   /MODELS=turbo,small
procedure ApplyCommandLineChoices;
var
  Engine, Models: String;
begin
  Engine := Lowercase(ExpandConstant('{param:engine|}'));
  if Engine = 'cpu' then
    EnginePage.SelectedValueIndex := ENGINE_CPU
  else if (Engine = 'cuda') or (Engine = 'gpu') then
    EnginePage.SelectedValueIndex := ENGINE_CUDA;

  Models := Lowercase(ExpandConstant('{param:models|}'));
  if Models <> '' then
  begin
    ModelPage.Values[MODEL_TURBO] := Pos('turbo', Models) > 0;
    ModelPage.Values[MODEL_SMALL] := Pos('small', Models) > 0;
    ModelPageVisited := True;
  end;
end;

procedure InitializeWizard;
begin
  DetectGpu;
  NeedVcRedist := VcRuntimeMissing;
  ModelPageVisited := False;

  EnginePage := CreateInputOptionPage(wpSelectDir,
    CustomMessage('EnginePageCaption'), CustomMessage('EnginePageDesc'),
    CustomMessage('EnginePageSub'), True, False);
  EnginePage.Add(CustomMessage('EngineCpu'));
  EnginePage.Add(CustomMessage('EngineCuda'));

  ModelPage := CreateInputOptionPage(EnginePage.ID,
    CustomMessage('ModelPageCaption'), CustomMessage('ModelPageDesc'),
    CustomMessage('ModelPageSub'), False, False);
  ModelPage.Add(CustomMessage('ModelTurbo'));
  ModelPage.Add(CustomMessage('ModelSmall'));

  if GpuState = 2 then
  begin
    EnginePage.SelectedValueIndex := ENGINE_CUDA;
    ModelPage.Values[MODEL_TURBO] := True;
  end
  else
  begin
    EnginePage.SelectedValueIndex := ENGINE_CPU;
    ModelPage.Values[MODEL_SMALL] := True;
  end;

  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing),
    SetupMessage(msgPreparingDesc), nil);
  DownloadPage.ShowBaseNameInsteadOfUrl := True;

  ApplyCommandLineChoices;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  Hint: String;
begin
  if CurPageID = EnginePage.ID then
  begin
    case GpuState of
      2: Hint := FmtMessage(CustomMessage('GpuFound'), [GpuDriver]);
      1: Hint := FmtMessage(CustomMessage('GpuOldDriver'), [GpuDriver]);
    else
      Hint := CustomMessage('GpuNotFound');
    end;
    EnginePage.SubCaptionLabel.Caption := Hint;
  end
  else if CurPageID = ModelPage.ID then
  begin
    ModelPageVisited := True;
    if TurboInstalled then
      ModelPage.CheckListBox.ItemCaption[0] :=
        FmtMessage(CustomMessage('ModelInstalled'), [CustomMessage('ModelTurbo')]);
    if SmallInstalled then
      ModelPage.CheckListBox.ItemCaption[1] :=
        FmtMessage(CustomMessage('ModelInstalled'), [CustomMessage('ModelSmall')]);
  end;
end;

function EnoughDiskSpace(NeededMB: Int64): Boolean;
var
  FreeBytes, TotalBytes: Int64;
  Msg: String;
begin
  Result := True;
  if GetSpaceOnDisk64(ExtractFileDrive(ExpandConstant('{app}')), FreeBytes, TotalBytes) then
    if FreeBytes < NeededMB * 1048576 then
    begin
      Msg := FmtMessage(CustomMessage('NoDiskSpace'), [IntToStr(NeededMB), IntToStr(FreeBytes div 1048576)]);
      if WizardSilent then
        Log('TRWhisper: ' + Msg)
      else
        MsgBox(Msg, mbError, MB_OK);
      Result := False;
    end;
end;

function RequiredMB: Int64;
begin
  Result := 200; // uygulama + VAD
  if UseCudaEngine then
    Result := Result + 1800  // indirilen zip + açılan dosyalar
  else
    Result := Result + 15;
  if WantTurbo and not TurboInstalled then Result := Result + 1100;
  if WantSmall and not SmallInstalled then Result := Result + 940;
end;

// Büyük model indirmelerinde geçici ağ hataları olabiliyor; 3 kez denenir.
function DownloadWithRetry: Boolean;
var
  Attempt: Integer;
  LastError: String;
begin
  Result := False;
  LastError := '';
  for Attempt := 1 to 3 do
  begin
    try
      DownloadPage.Download;
      Result := True;
      Exit;
    except
      if DownloadPage.AbortedByUser then
      begin
        Log('TRWhisper: indirme kullanici tarafindan iptal edildi.');
        Exit;
      end;
      LastError := DownloadPage.LastBaseNameOrUrl + ': ' + GetExceptionMessage;
      Log('TRWhisper: indirme hatasi (deneme ' + IntToStr(Attempt) + '/3): ' + LastError);
      if Attempt < 3 then
        Sleep(3000);
    end;
  end;

  // Sessiz kurulumda diyalog gösterilemez (kullanıcı yanıtlayamaz), yalnızca günlüğe yazılır.
  if not WizardSilent then
    SuppressibleMsgBox(AddPeriod(LastError), mbCriticalError, MB_OK, IDOK);
end;

function PrepareDownloads: Boolean;
begin
  Result := True;
  DownloadBytes := 0;
  CudaDownloaded := False;
  DownloadPage.Clear;

  if NeedVcRedist then
  begin
    // Microsoft bu dosyayı güncellediği için SHA-256 sabitlenemez (imzası kurulumda doğrulanır).
    DownloadPage.Add('{#VcRedistUrl}', 'vc_redist.x64.exe', '');
    DownloadBytes := DownloadBytes + {#VcRedistSize};
  end;

  if UseCudaEngine and not CudaInstalled then
  begin
    DownloadPage.Add('{#CudaZipUrl}', '{#CudaZipName}', '{#CudaZipSha}');
    DownloadBytes := DownloadBytes + {#CudaZipSize};
    CudaDownloaded := True;
  end;

  if WantTurbo and not TurboInstalled then
  begin
    DownloadPage.Add('{#TurboUrl}', '{#TurboName}', '{#TurboSha}');
    DownloadBytes := DownloadBytes + {#TurboSize};
  end;

  if WantSmall and not SmallInstalled then
  begin
    DownloadPage.Add('{#SmallUrl}', '{#SmallName}', '{#SmallSha}');
    DownloadBytes := DownloadBytes + {#SmallSize};
  end;

  if DownloadBytes = 0 then
    Exit;

  if not WizardSilent then
    DownloadPage.Show;
  try
    Result := DownloadWithRetry;
  finally
    if not WizardSilent then
      DownloadPage.Hide;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = EnginePage.ID then
  begin
    if UseCudaEngine and (GpuState = 0) then
      if MsgBox(CustomMessage('GpuConfirm'), mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    // Kullanıcı model sayfasını hiç görmediyse motora uygun varsayılanı ayarla.
    if not ModelPageVisited then
    begin
      ModelPage.Values[MODEL_TURBO] := UseCudaEngine or TurboInstalled;
      ModelPage.Values[MODEL_SMALL] := UseCpuEngine or SmallInstalled;
    end;
  end

  else if CurPageID = ModelPage.ID then
  begin
    if not (WantTurbo or WantSmall) then
    begin
      MsgBox(CustomMessage('ModelNoneSelected'), mbError, MB_OK);
      Result := False;
      Exit;
    end;
    if UseCpuEngine and WantTurbo and not WantSmall then
      if MsgBox(CustomMessage('ModelTurboOnCpu'), mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
  end

  else if CurPageID = wpReady then
  begin
    Result := EnoughDiskSpace(RequiredMB) and PrepareDownloads;
  end;
end;

function FormatMB(Bytes: Int64): String;
begin
  Result := IntToStr((Bytes + 524288) div 1048576) + ' MB';
end;

function UpdateReadyMemo(const Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  S, Models, Downloads: String;
  Total: Int64;
begin
  S := MemoDirInfo + NewLine + NewLine;

  S := S + CustomMessage('MemoEngine') + NewLine + Space;
  if UseCudaEngine then
    S := S + CustomMessage('EngineCuda') + NewLine + NewLine
  else
    S := S + CustomMessage('EngineCpu') + NewLine + NewLine;

  Models := '';
  if WantTurbo then Models := Models + Space + CustomMessage('ModelTurbo') + NewLine;
  if WantSmall then Models := Models + Space + CustomMessage('ModelSmall') + NewLine;
  S := S + CustomMessage('MemoModels') + NewLine + Models + NewLine;

  S := S + CustomMessage('MemoAlways') + NewLine + Space + CustomMessage('MemoAlwaysItems') + NewLine + NewLine;

  Total := 0;
  Downloads := '';
  if NeedVcRedist then
  begin
    Downloads := Downloads + Space + CustomMessage('MemoVcRedist') + NewLine;
    Total := Total + {#VcRedistSize};
  end;
  if UseCudaEngine and not CudaInstalled then
  begin
    Downloads := Downloads + Space + '{#CudaZipName} (' + FormatMB({#CudaZipSize}) + ')' + NewLine;
    Total := Total + {#CudaZipSize};
  end;
  if WantTurbo and not TurboInstalled then
  begin
    Downloads := Downloads + Space + '{#TurboName} (' + FormatMB({#TurboSize}) + ')' + NewLine;
    Total := Total + {#TurboSize};
  end;
  if WantSmall and not SmallInstalled then
  begin
    Downloads := Downloads + Space + '{#SmallName} (' + FormatMB({#SmallSize}) + ')' + NewLine;
    Total := Total + {#SmallSize};
  end;

  if Total = 0 then
    Downloads := Space + CustomMessage('MemoDownloadNone') + NewLine
  else
    Downloads := Downloads + Space + '= ' + FormatMB(Total) + NewLine;
  S := S + CustomMessage('MemoDownload') + NewLine + Downloads;

  if MemoTasksInfo <> '' then
    S := S + NewLine + MemoTasksInfo;

  Result := S;
end;

{ ---------- Kurulum adımları ---------- }

procedure InstallCudaFiles;
var
  TempDir, Src, Dest, FileName: String;
  Names: TArrayOfString;
  I: Integer;
  FindRec: TFindRec;
begin
  TempDir := ExpandConstant('{app}\tools\whisper\_cuda_tmp');
  WizardForm.StatusLabel.Caption := CustomMessage('StatusExtractCuda');
  try
    ExtractArchive(ExpandConstant('{tmp}\{#CudaZipName}'), TempDir, '', True, nil);
  except
    MsgBox(FmtMessage(CustomMessage('ExtractFailed'), [GetExceptionMessage]), mbError, MB_OK);
    DelTree(TempDir, True, True, True);
    Exit;
  end;

  SetArrayLength(Names, 8);
  Names[0] := 'whisper-cli.exe';
  Names[1] := 'whisper.dll';
  Names[2] := 'ggml.dll';
  Names[3] := 'ggml-base.dll';
  Names[4] := 'ggml-cuda.dll';
  Names[5] := 'cublas64_12.dll';
  Names[6] := 'cublasLt64_12.dll';
  Names[7] := 'cudart64_12.dll';

  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    Src := TempDir + '\Release\' + Names[I];
    Dest := ToolsDir + '\' + Names[I];
    DeleteFile(Dest);
    if not RenameFile(Src, Dest) then
      Log('TRWhisper: CUDA dosyasi tasinamadi: ' + Names[I]);
  end;

  // ggml-cpu-*.dll (CUDA derlemesinde de gerekli: GPU yoksa CPU'ya döner)
  if FindFirst(TempDir + '\Release\ggml-cpu-*.dll', FindRec) then
  try
    repeat
      FileName := FindRec.Name;
      DeleteFile(ToolsDir + '\' + FileName);
      RenameFile(TempDir + '\Release\' + FileName, ToolsDir + '\' + FileName);
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;

  DelTree(TempDir, True, True, True);
end;

procedure InstallVcRedist;
var
  Installer: String;
  ResultCode: Integer;
begin
  Installer := ExpandConstant('{tmp}\vc_redist.x64.exe');
  if not FileExists(Installer) then
    Exit;

  WizardForm.StatusLabel.Caption := CustomMessage('StatusVcRedist');
  SuppressibleMsgBox(CustomMessage('VcRedistPrompt'), mbInformation, MB_OK, IDOK);

  if not ShellExec('runas', Installer, '/install /passive /norestart', '',
       SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    ResultCode := -1;

  Log('TRWhisper: vc_redist sonuc=' + IntToStr(ResultCode));
  // 0 = tamam, 1638 = daha yenisi kurulu, 3010 = yeniden başlatma gerekiyor
  if not ((ResultCode = 0) or (ResultCode = 1638) or (ResultCode = 3010)) then
    if VcRuntimeMissing then
      SuppressibleMsgBox(CustomMessage('VcRedistFailed'), mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if WizardSilent then
    if not (EnoughDiskSpace(RequiredMB) and PrepareDownloads) then
      Result := 'Setup could not download the required files.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if UseCudaEngine and CudaDownloaded then
      InstallCudaFiles;
    if NeedVcRedist then
      InstallVcRedist;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DictationDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DictationDir := ExpandConstant('{%USERPROFILE}\Dictation');
    // Sessiz kaldırmada soru sorulamayacağı için dikte kayıtları her zaman korunur.
    if DirExists(DictationDir) and not UninstallSilent then
      if SuppressibleMsgBox(FmtMessage(CustomMessage('UninstallKeepData'), [DictationDir]),
           mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(DictationDir, True, True, True);
  end;
end;
