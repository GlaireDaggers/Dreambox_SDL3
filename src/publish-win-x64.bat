dotnet publish -c Release -r win-x64 --output .\publish\dreambox-win-x64\ DreamboxVM.csproj || exit /b
robocopy .\content .\publish\dreambox-win-x64\content /s
robocopy .\native-libs\win-x64 .\publish\dreambox-win-x64
tar -cvz -f .\publish\dreambox-win-x64.tar.gz -C .\publish dreambox-win-x64