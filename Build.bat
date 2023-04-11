mklink /j Packages ..\WhenTheVersion\Packages
del ..\WhenTheVersion\Packages\Net4x.Serilog.Sinks.File.*
rmdir /s /q %userprofile%\.nuget\Packages\Net4x.Serilog.Sinks.File
del "src\Serilog.Sinks.File\bin\%Configuration%\Net4x.Serilog.Sinks.File.*"
MSBuild.exe serilog-sinks-file.sln -t:clean
MSBuild.exe serilog-sinks-file.sln -t:restore -p:RestorePackagesConfig=true
MSBuild.exe serilog-sinks-file.sln -m /property:Configuration=%Configuration% 
git add -A
git commit -a --allow-empty-message -m ''
git push
