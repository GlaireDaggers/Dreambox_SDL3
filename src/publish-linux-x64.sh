set -e
dotnet publish -c Release -r linux-x64 --output ./publish/dreambox-linux-x64/ DreamboxVM.csproj
cp -R ./content ./publish/dreambox-linux-x64/
cp -R -a ./native-libs/linux-x64/. ./publish/dreambox-linux-x64
cp icon.qoi ./publish/dreambox-linux-x64
rm -f ./publish/dreambox-linux-x64/DreamboxVM.dbg