#!/bin/sh
# Build CombatTrainer.dll with the C# compiler that ships with Windows (C# 5). Usage: sh build.sh
# Set VALHEIM_DIR and BEPINEX_CORE (or put them in build.local.sh, which git ignores).
[ -f build.local.sh ] && . ./build.local.sh
VALHEIM_DIR="${VALHEIM_DIR:-C:/Program Files (x86)/Steam/steamapps/common/Valheim}"
BEPINEX_CORE="${BEPINEX_CORE:-$APPDATA/r2modmanPlus-local/Valheim/profiles/Default/BepInEx/core}"
M="$VALHEIM_DIR/valheim_Data/Managed"
CSC="/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
set --
for r in mscorlib netstandard System System.Core assembly_valheim assembly_utils assembly_guiutils gui_framework UnityEngine UnityEngine.CoreModule UnityEngine.IMGUIModule UnityEngine.TextRenderingModule UnityEngine.UI UnityEngine.UIModule Unity.TextMeshPro UnityEngine.PhysicsModule UnityEngine.InputLegacyModule SoftReferenceableAssets; do set -- "$@" "-r:$M/$r.dll"; done
"$CSC" -nologo -noconfig -nostdlib -target:library -optimize -out:CombatTrainer.dll "$@" -r:"$BEPINEX_CORE/BepInEx.dll" -r:"$BEPINEX_CORE/0Harmony.dll" CombatTrainer.cs Kits.cs Waves.cs Gauntlet.cs
