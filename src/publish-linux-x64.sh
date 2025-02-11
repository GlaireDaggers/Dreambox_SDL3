set -e
dotnet publish -c Release -r linux-x64 --output ./publish/dreambox-linux-x64/ DreamboxVM.csproj
cp -R ./content ./publish/dreambox-linux-x64/
cp -R -a ./native-libs/linux-x64/. ./publish/dreambox-linux-x64
rm -rf ./publish/dreambox-linux-x64.tar.gz
tar -cvz -f ./publish/dreambox-linux-x64.tar.gz -C ./publish dreambox-linux-x64