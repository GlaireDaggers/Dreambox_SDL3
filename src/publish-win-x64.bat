dotnet publish -c Release -r win-x64 --output .\publish\dreambox-win-x64\ DreamboxVM.csproj || exit /b
robocopy .\content .\publish\dreambox-win-x64 /y /s || exit /b
robocopy .\native-libs\win-x64\*.dll .\publish\dreambox-win-x64 /y || exit /b
tar -cvz -f .\publish\dreambox-win-x64.tar.gz -C .\publish dreambox-win-x64 || exit /b