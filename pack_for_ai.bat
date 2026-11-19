@echo off
del /q ai_handoff.zip 2>nul
powershell -Command "$paths = @('AI_CONTEXT.md','README.md','Source\Assets\Scripts','Source\Assets\Scenes','Source\Packages\manifest.json','Source\ProjectSettings\TagManager.asset') | Where-Object { Test-Path $_ }; Compress-Archive -Path $paths -DestinationPath 'ai_handoff.zip' -Force"
echo Done! Upload ai_handoff.zip to the next AI.
pause