set -e
dotnet publish -c Release -r linux-x64 --output ./publish/linux-x64/ DreamboxVM.csproj
cp -R ./content ./publish/linux-x64/
cp -R -a ./native-libs/linux-x64/. ./publish/linux-x64