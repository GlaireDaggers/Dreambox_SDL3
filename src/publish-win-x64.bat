dotnet publish -c Release -r win-x64 --output .\publish\dreambox-win-x64\ DreamboxVM.csproj || exit /b
robocopy .\content .\publish\dreambox-win-x64\content /s
robocopy .\native-libs\win-x64 .\publish\dreambox-win-x64
xcopy /y .\icon.qoi .\publish\dreambox-win-x64\icon.qoi