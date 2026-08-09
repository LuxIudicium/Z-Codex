; ============================================================================
;  Z-Codex — script d'installation (Inno Setup 6)
;
;  Ne pas compiler ce fichier directement : il consomme la sortie de
;  `dotnet publish` (profil win-x64-selfcontained) et les bitmaps produits par
;  make-wizard-images.ps1. Passer par build.ps1, qui enchaîne les trois étapes.
;
;  Choix structurants :
;
;  * Installation PAR UTILISATEUR ({autopf} devient %LocalAppData%\Programs avec
;    PrivilegesRequired=lowest). Aucune élévation UAC. C'est cohérent avec le
;    reste : les données de Z-Codex vivent déjà dans %AppData%\Z-Codex, qui est
;    par utilisateur. Une installation administrateur ne préparerait rien pour le
;    compte du joueur, qui referait de toute façon son téléchargement du wiki.
;
;  * Charge AUTONOME : le .NET 8 Desktop Runtime est dans la sortie de publication,
;    d'où les ~155 Mo empaquetés. Le poste cible n'a aucun prérequis à installer.
;
;  * AUCUN ASSET DE JEU n'est empaqueté ici. Les icônes et textes de compétences
;    sont téléchargés au premier lancement depuis le Guild Wars Wiki public, sur la
;    machine du joueur. Voir LICENSE, section THIRD-PARTY NOTICES.
; ============================================================================

#define AppName       "Z-Codex"
#define AppPublisher  "P. Vincent"
#define AppExeName    "Z-Codex.exe"

#define Root          AddBackslash(SourcePath) + ".."
#define PayloadDir    Root + "\src\ZCodex.App\bin\publish\win-x64"

; La version affichée partout (assistant, « Applications installées », propriétés du
; setup) est LUE dans l'exécutable publié, donc dérivée du <Version> du .csproj.
; Rien à tenir à jour en double ici.
#define AppVersion    GetStringFileInfo(PayloadDir + "\" + AppExeName, "ProductVersion")

[Setup]
; Identifiant stable de l'application : c'est lui, et non le nom, qui permet à une
; version ultérieure de se reconnaître comme une mise à jour. NE JAMAIS LE CHANGER.
AppId={{835B3B79-9901-4FDB-9887-CA2C8B6C25F3}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright=© 2026 P. Vincent — licence MIT. Guild Wars © ArenaNet/NCSOFT.
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
; Le runtime embarqué est win-x64 : refuser tout de suite un poste 32 bits plutôt
; que de laisser l'utilisateur découvrir l'incompatibilité au premier lancement.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Plancher de .NET 8 (Windows 10 1607).
MinVersion=10.0.14393

; PAS le LICENSE de la racine : Inno traite un fichier de licence sans BOM comme de
; l'ANSI, et les trois « ² » de « paw*ned² » s'afficheraient « Â² » sur la page de
; licence. build.ps1 en dépose une copie signée UTF-8 dans obj\.
LicenseFile={#AddBackslash(SourcePath)}obj\LICENSE.txt
SetupIconFile={#Root}\src\ZCodex.App\Assets\Z-Codex.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

OutputDir={#AddBackslash(SourcePath)}output
OutputBaseFilename={#AppName}-{#AppVersion}-setup
; ultra64 sur une charge très redondante (269 fichiers de runtime .NET) : la
; compression est lente à produire mais divise le téléchargement du joueur.
Compression=lzma2/ultra64
SolidCompression=yes

WizardStyle=modern
; Inno masque la page d'accueil par défaut en style moderne, ce qui escamoterait
; le bandeau illustré. On la remet.
DisableWelcomePage=no
WizardImageFile={#AddBackslash(SourcePath)}assets\WizardImage.bmp,{#AddBackslash(SourcePath)}assets\WizardImage-125.bmp,{#AddBackslash(SourcePath)}assets\WizardImage-150.bmp,{#AddBackslash(SourcePath)}assets\WizardImage-175.bmp,{#AddBackslash(SourcePath)}assets\WizardImage-200.bmp
WizardSmallImageFile={#AddBackslash(SourcePath)}assets\WizardSmallImage.bmp,{#AddBackslash(SourcePath)}assets\WizardSmallImage-125.bmp,{#AddBackslash(SourcePath)}assets\WizardSmallImage-150.bmp,{#AddBackslash(SourcePath)}assets\WizardSmallImage-175.bmp,{#AddBackslash(SourcePath)}assets\WizardSmallImage-200.bmp

[Languages]
Name: "french";  MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; La licence voyage avec l'application : elle porte les mentions ArenaNet/NCSOFT et
; les licences des composants tiers embarqués. On installe la copie BOMée pour la même
; raison que ci-dessus — le Bloc-notes des vieilles éditions de Windows ne devine pas
; l'UTF-8 sans marque.
Source: "{#AddBackslash(SourcePath)}obj\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\README.md";     DestDir: "{app}"; Flags: ignoreversion
Source: "{#Root}\README.en.md";  DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";                        Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}";  Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";                  Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Messages]
; Le premier lancement prend cinq minutes de téléchargement du wiki. Sans un mot ici,
; l'utilisateur lance l'application et croit qu'elle est plantée.
french.FinishedLabel=L'installation de [name] est terminée.%n%nAu tout premier lancement, Z-Codex télécharge le catalogue des compétences depuis le Guild Wars Wiki : comptez environ cinq minutes, avec une fenêtre de progression à l'écran. Cela n'a lieu qu'une fois — ensuite l'application démarre en quelques secondes et fonctionne hors ligne.
english.FinishedLabel=Setup has finished installing [name] on your computer.%n%nOn the very first launch, Z-Codex downloads the skill catalogue from the Guild Wars Wiki: expect about five minutes, with a progress window on screen. This happens only once — afterwards the application starts in seconds and works offline.

[CustomMessages]
french.RemoveDataPrompt=Conserver vos builds et le catalogue des compétences téléchargé ?%n%n%1%n%nRépondez Oui pour les garder (utile si vous comptez réinstaller Z-Codex : le téléchargement du wiki ne sera pas à refaire).%nRépondez Non pour tout supprimer définitivement.
english.RemoveDataPrompt=Keep your builds and the downloaded skill catalogue?%n%n%1%n%nAnswer Yes to keep them (useful if you plan to reinstall Z-Codex: the wiki download will not have to be done again).%nAnswer No to delete everything permanently.
french.RemoveDataTitle=Données de Z-Codex
english.RemoveDataTitle=Z-Codex data

[Code]
{ Les builds et le catalogue vivent dans %AppData%\Z-Codex, hors du dossier
  d'installation : le désinstalleur ne les voit donc pas et les laisserait orphelins
  (plusieurs centaines de Mo une fois les icônes téléchargées). On pose la question
  plutôt que de trancher — la réponse par défaut est CONSERVER, parce que supprimer
  par erreur le travail de l'utilisateur est irrattrapable, alors que quelques Mo
  oubliés ne coûtent rien. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\Z-Codex');
    if DirExists(DataDir) then
    begin
      { SuppressibleMsgBox et non MsgBox : ce dernier s'affiche même sous /SILENT et
        bloquerait une désinstallation sans surveillance sur une boîte de dialogue que
        personne ne voit. Le dernier paramètre est la réponse retenue quand les boîtes
        sont supprimées — IDYES, donc « conserver ». }
      if SuppressibleMsgBox(FmtMessage(CustomMessage('RemoveDataPrompt'), [DataDir]),
                            mbConfirmation, MB_YESNO or MB_DEFBUTTON1, IDYES) = IDNO then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
