@echo off
del /q ai_handoff.zip 2>nul
powershell -Command "Compress-Archive -Path 'AI_CONTEXT.md','README.md','Source\Assets\Scripts','Source\ProjectSettings\TagManager.asset' -DestinationPath 'ai_handoff.zip' -Force"
echo Ready! Upload ai_handoff.zip to the next AI.
pause
