properties {
	$base_dir  = resolve-path .
	$lib_dir = "$base_dir\SharedLibs"
	$build_dir = "$base_dir\build"
	$buildartifacts_dir = "$build_dir\"
	$sln_file = "$base_dir\zzz_RavenDB_Release.sln"
	$version = "1.0"
	$tools_dir = "$base_dir\Tools"
	$release_dir = "$base_dir\Release"
	$uploader = "..\Uploader\S3Uploader.exe"
  
	$web_dlls = @( "Raven.Abstractions.???","Raven.Web.???", (Get-DependencyPackageFiles 'NLog.2'), (Get-DependencyPackageFiles Newtonsoft.Json), (Get-DependencyPackageFiles Microsoft.Web.Infrastructure), 
				"Lucene.Net.???", "Lucene.Net.Contrib.Spatial.???", "Lucene.Net.Contrib.SpellChecker.???","BouncyCastle.Crypto.???",
				"ICSharpCode.NRefactory.???", "Rhino.Licensing.???", "Esent.Interop.???", "Raven.Database.???", "Raven.Storage.Esent.???", 
				"Raven.Storage.Managed.???", "Raven.Munin.???" ) |
		ForEach-Object { 
			if ([System.IO.Path]::IsPathRooted($_)) { return $_ }
			return "$build_dir\$_"
		}
	
	$web_files = @("Raven.Studio.xap", "..\DefaultConfigs\web.config" )
	
	$server_files = @( "Raven.Server.exe", "Raven.Studio.xap", (Get-DependencyPackageFiles 'NLog.2'), (Get-DependencyPackageFiles Newtonsoft.Json), "Lucene.Net.???",
					 "Lucene.Net.Contrib.Spatial.???", "Lucene.Net.Contrib.SpellChecker.???", "ICSharpCode.NRefactory.???", "Rhino.Licensing.???", "BouncyCastle.Crypto.???",
					"Esent.Interop.???", "Raven.Abstractions.???", "Raven.Database.???", "Raven.Storage.Esent.???",
					"Raven.Storage.Managed.???", "Raven.Munin.???" ) |
		ForEach-Object { 
			if ([System.IO.Path]::IsPathRooted($_)) { return $_ }
			return "$build_dir\$_"
		}
		
	$client_dlls_3_5 = @( (Get-DependencyPackageFiles 'NLog.2' -FrameworkVersion net35), (Get-DependencyPackageFiles Newtonsoft.Json -FrameworkVersion net35), "Raven.Abstractions-3.5.???", "Raven.Client.Lightweight-3.5.???") |
		ForEach-Object { 
			if ([System.IO.Path]::IsPathRooted($_)) { return $_ }
			return "$build_dir\$_"
		}
	 
	$client_dlls = @( (Get-DependencyPackageFiles 'NLog.2'), "Raven.Client.MvcIntegration.???", (Get-DependencyPackageFiles Newtonsoft.Json),
					"Raven.Abstractions.???", "Raven.Client.Lightweight.???", "Raven.Client.Lightweight.FSharp.???", "Raven.Client.Debug.???") |
		ForEach-Object { 
			if ([System.IO.Path]::IsPathRooted($_)) { return $_ }
			return "$build_dir\$_"
		}
  
	$silverlight4_dlls = @( "Raven.Client.Silverlight-4.???", "AsyncCtpLibrary_Silverlight.???", 
						(Get-DependencyPackageFiles Newtonsoft.Json -FrameworkVersion sl4), (Get-DependencyPackageFiles 'NLog.2' -FrameworkVersion sl4)) |
		ForEach-Object { 
			if ([System.IO.Path]::IsPathRooted($_)) { return $_ }
			return "$build_dir\$_"
		}
		
	$silverlight_dlls = @( "Raven.Client.Silverlight.???", "AsyncCtpLibrary_Silverlight5.???", 
						(Get-DependencyPackageFiles Newtonsoft.Json -FrameworkVersion sl4), (Get-DependencyPackageFiles 'NLog.2' -FrameworkVersion sl4)) |
		ForEach-Object { 
			if ([System.IO.Path]::IsPathRooted($_)) { return $_ }
			return "$build_dir\$_"
		}
 
	$all_client_dlls = @( "Raven.Client.MvcIntegration.???", "Raven.Client.Lightweight.???", "Raven.Client.Lightweight.FSharp.???", "Raven.Client.Embedded.???", "Raven.Abstractions.???", "Raven.Database.???", "BouncyCastle.Crypto.???",
						  "Esent.Interop.???", "ICSharpCode.NRefactory.???", "Lucene.Net.???", "Lucene.Net.Contrib.Spatial.???",
						  "Lucene.Net.Contrib.SpellChecker.???", (Get-DependencyPackageFiles 'NLog.2'), (Get-DependencyPackageFiles Newtonsoft.Json),
						  "Raven.Storage.Esent.???", "Raven.Storage.Managed.???", "Raven.Munin.???", "AsyncCtpLibrary.???", "Raven.Studio.xap"  ) |
		ForEach-Object { 
			if ([System.IO.Path]::IsPathRooted($_)) { return $_ }
			return "$build_dir\$_"
		}
	  
	$test_prjs = @("Raven.Tests.dll","Raven.Tests.FSharp.dll", "Raven.Client.VisualBasic.Tests.dll", "Raven.Bundles.Tests.dll" )
}
include .\psake_ext.ps1

task default -depends OpenSource,Release

task Verify40 {
	if( (ls "$env:windir\Microsoft.NET\Framework\v4.0*") -eq $null ) {
		throw "Building Raven requires .NET 4.0, which doesn't appear to be installed on this machine"
	}
}


task Clean {
  remove-item -force -recurse $buildartifacts_dir -ErrorAction SilentlyContinue
  remove-item -force -recurse $release_dir -ErrorAction SilentlyContinue
}

task Init -depends Verify40, Clean {

	if($env:BUILD_NUMBER -ne $null) {
		$env:buildlabel  = $env:BUILD_NUMBER
	}
	if($env:buildlabel -eq $null) {
		$env:buildlabel = "13"
	}
	
	if($env:buildlabel -ne 13) {
		$projectFiles = Get-ChildItem -Path $base_dir -Filter "*.csproj" -Recurse | 
							Where-Object { $_.Directory -notmatch [regex]::Escape($lib_dir) } | 
							Where-Object { $_.Directory -notmatch [regex]::Escape($tools_dir) }
		
		$notclsCompliant = @("Raven.Silverlight.Client", "Raven.Studio", "Raven.Tests.Silverlight")
		
		foreach($projectFile in $projectFiles) {
			
			$projectName = [System.IO.Path]::GetFileName($projectFile.Directory)
			$asmInfo = [System.IO.Path]::Combine($projectFile.Directory, [System.IO.Path]::Combine("Properties", "AssemblyInfo.cs"))
			
			$clsComliant = "true"
			if([System.Array]::IndexOf($notclsCompliant, $projectFile.Name) -ne -1) {
				$clsComliant = "false"
			}
			
			Generate-Assembly-Info `
				-file $asmInfo `
				-title "$projectName $version.0.0" `
				-description "A linq enabled document database for .NET" `
				-company "Hibernating Rhinos" `
				-product "RavenDB $version.0.0" `
				-version "$version.0" `
				-fileversion "$version.$env:buildlabel.0" `
				-copyright "Copyright � Hibernating Rhinos 2004 - $((Get-Date).Year)" `
				-clsCompliant $clsComliant
		}
	}
	
	
	New-Item $release_dir -itemType directory -ErrorAction SilentlyContinue | Out-Null
	New-Item $build_dir -itemType directory -ErrorAction SilentlyContinue | Out-Null
	
	copy $tools_dir\xUnit\*.* $build_dir
}

task BeforeCompile {
	$dat = "$base_dir\..\BuildsInfo\RavenDB\Settings.dat"
	$datDest = "$base_dir\Raven.Studio\Settings.dat"
	echo $dat
	if (Test-Path $dat) {
		Copy-Item $dat $datDest -force
	}
	ElseIf ((Test-Path $datDest) -eq $false) {
		New-Item $datDest -type file -force
	}
}

task AfterCompile {
	#new-item "$base_dir\Raven.Studio\Settings.dat" -type file -force
}

task Compile -depends Init {
	
	$v4_net_version = (ls "$env:windir\Microsoft.NET\Framework\v4.0*").Name
	
	exec { &"C:\Windows\Microsoft.NET\Framework\$v4_net_version\MSBuild.exe" "$base_dir\Utilities\Raven.ProjectRewriter\Raven.ProjectRewriter.csproj" /p:OutDir="$buildartifacts_dir\" }
	exec { &"$build_dir\Raven.ProjectRewriter.exe" }
	
	try { 
		ExecuteTask("BeforeCompile")
		exec { &"C:\Windows\Microsoft.NET\Framework\$v4_net_version\MSBuild.exe" "$sln_file" /p:OutDir="$buildartifacts_dir\" }
	} catch {
		Throw
	} finally { 
		ExecuteTask("AfterCompile")
	}
}

task Test -depends Compile {
	Write-Host $test_prjs
	$test_prjs | ForEach-Object { 
		Write-Host "Testing $build_dir\$_"
		exec { &"$build_dir\xunit.console.clr4.exe" "$build_dir\$_" }
	}
}

task StressTest -depends Compile {
	Copy-Item (Get-DependencyPackageFiles 'NLog.2') $build_dir -force
	Copy-Item (Get-DependencyPackageFiles Newtonsoft.Json) $build_dir -force
	
	@("Raven.StressTests.dll") | ForEach-Object { 
		Write-Host "Testing $build_dir\$_"
		exec { &"$build_dir\xunit.console.clr4.exe" "$build_dir\$_" }
	}
}

task MeasurePerformance -depends Compile {
	$RavenDbStableLocation = "F:\RavenDB"
	$DataLocation = "F:\Data"
	$LogsLocation = "F:\PerformanceLogs"
	$stableBuildToTests = @(616, 573, 531, 499, 482, 457, 371)
	$stableBuildToTests | ForEach-Object { 
		$RavenServer = $RavenDbStableLocation + "\RavenDB-Build-$_\Server"
		Write-Host "Measure performance against RavenDB Build #$_, Path: $RavenServer"
		exec { &"$build_dir\Raven.Performance.exe" "--database-location=$RavenDbStableLocation --build-number=$_ --data-location=$DataLocation --logs-location=$LogsLocation" }
	}
}

task TestSilverlight -depends Compile, CopyServer {
	try
	{
		start "$build_dir\Output\Server\Raven.Server.exe" "--ram --set=Raven/Port==8079"
		exec { & ".\Tools\StatLight\StatLight.exe" "-x=.\build\Raven.Tests.Silverlight.xap" "--OverrideTestProvider=MSTestWithCustomProvider" "--ReportOutputFile=.\Raven.Tests.Silverlight.Results.xml" }
	}
	finally
	{
		ps "Raven.Server" | kill
	}
}

task ReleaseNoTests -depends OpenSource,DoRelease {

}

task Unstable {
	$global:uploadCategory = "RavenDB-Unstable"
}

task OpenSource {
	$global:uploadCategory = "RavenDB"
}

task RunTests -depends Test,TestSilverlight

task RunAllTests -depends Test,TestSilverlight,StressTest

task Release -depends RunTests,DoRelease

task CopySamples {
	$samples = @("Raven.Sample.ShardClient", "Raven.Sample.Failover", "Raven.Sample.Replication", `
			   "Raven.Sample.EventSourcing", "Raven.Bundles.Sample.EventSourcing.ShoppingCartAggregator", `
			   "Raven.Samples.IndexReplication", "Raven.Samples.Includes", "Raven.Sample.SimpleClient", `
			   "Raven.Sample.MultiTenancy", "Raven.Sample.Suggestions", `
			   "Raven.Sample.LiveProjections", "Raven.Sample.FullTextSearch")
	$exclude = @("bin", "obj", "Data", "Plugins")
	
	foreach ($sample in $samples) {
	  echo $sample 
	  
	  Delete-Sample-Data-For-Release "$base_dir\Samples\$sample"
	  
	  cp "$base_dir\Samples\$sample" "$build_dir\Output\Samples" -recurse -force
	  
	  Delete-Sample-Data-For-Release "$build_dir\Output\Samples\$sample" 
	}
	
	cp "$base_dir\Samples\Raven.Samples.sln" "$build_dir\Output\Samples" -force
	cp "$base_dir\Samples\Samples.ps1" "$build_dir\Output\Samples" -force
	  
	exec { .\Utilities\Binaries\Raven.Samples.PrepareForRelease.exe "$build_dir\Output\Samples\Raven.Samples.sln" "$build_dir\Output" }
}

task CreateOutpuDirectories -depends CleanOutputDirectory {
	New-Item $build_dir\Output -Type directory | Out-Null
	New-Item $build_dir\Output\Web -Type directory | Out-Null
	New-Item $build_dir\Output\Web\bin -Type directory | Out-Null
	New-Item $build_dir\Output\EmbeddedClient -Type directory | Out-Null
	New-Item $build_dir\Output\Client -Type directory | Out-Null
	New-Item $build_dir\Output\Client-3.5 -Type directory | Out-Null
	New-Item $build_dir\Output\Silverlight -Type directory | Out-Null
	New-Item $build_dir\Output\Silverlight-4 -Type directory | Out-Null
	New-Item $build_dir\Output\Bundles -Type directory | Out-Null
	New-Item $build_dir\Output\Samples -Type directory | Out-Null
	New-Item $build_dir\Output\Smuggler -Type directory | Out-Null
	New-Item $build_dir\Output\Backup -Type directory | Out-Null
}

task CleanOutputDirectory { 
	Remove-Item $build_dir\Output -Recurse -Force  -ErrorAction SilentlyContinue
}

task CopyEmbeddedClient { 
	$all_client_dlls | ForEach-Object { Copy-Item "$_" $build_dir\Output\EmbeddedClient }
}

task CopySilverlight { 
	$silverlight_dlls | ForEach-Object { Copy-Item "$_" $build_dir\Output\Silverlight }
}

task CopySilverlight-4 { 
	$silverlight4_dlls | ForEach-Object { Copy-Item "$_" $build_dir\Output\Silverlight-4 }
}

task CopySmuggler {
	Copy-Item $build_dir\Raven.Abstractions.??? $build_dir\Output\Smuggler
	Copy-Item (Get-DependencyPackageFiles Newtonsoft.Json) $build_dir\Output\Smuggler
	Copy-Item $build_dir\Raven.Smuggler.??? $build_dir\Output\Smuggler
}

task CopyBackup {
	Copy-Item $build_dir\Raven.Backup.??? $build_dir\Output\Backup
	Copy-Item (Get-DependencyPackageFiles Newtonsoft.Json) $build_dir\Output\Backup
}

task CopyClient {
	$client_dlls | ForEach-Object { Copy-Item "$_" $build_dir\Output\Client }
}

task CopyClient35 {
	$client_dlls_3_5 | ForEach-Object { Copy-Item "$_" $build_dir\Output\Client-3.5 }
}

task CopyWeb {
	$web_dlls | ForEach-Object { Copy-Item "$_" $build_dir\Output\Web\bin }
	$web_files | ForEach-Object { Copy-Item "$build_dir\$_" $build_dir\Output\Web }
}

task CopyBundles {
	$items = (Get-ChildItem $build_dir\Raven.Bundles.*.???) + (Get-ChildItem $build_dir\Raven.Client.*.???) | 
				Where-Object { $_.Name.Contains(".Tests.") -eq $false } | ForEach-Object { $_.FullName }
	Copy-Item $items $build_dir\Output\Bundles
}

task CopyServer {
	New-Item $build_dir\Output\Server -Type directory | Out-Null
	$server_files | ForEach-Object { Copy-Item "$_" $build_dir\Output\Server }
	Copy-Item $base_dir\DefaultConfigs\RavenDb.exe.config $build_dir\Output\Server\Raven.Server.exe.config
}

task CreateDocs {
	$v4_net_version = (ls "$env:windir\Microsoft.NET\Framework\v4.0*").Name
	
	if($env:buildlabel -eq 13)
	{
	  return 
	}
	 
  # we expliclty allows this to fail
  & "C:\Windows\Microsoft.NET\Framework\$v4_net_version\MSBuild.exe" "$base_dir\Raven.Docs.shfbproj" /p:OutDir="$buildartifacts_dir\"
}

task CopyRootFiles -depends CreateDocs {
	cp $base_dir\license.txt $build_dir\Output\license.txt
	cp $base_dir\Scripts\Start.cmd $build_dir\Output\Start.cmd
	cp $base_dir\Scripts\Raven-UpdateBundles.ps1 $build_dir\Output\Raven-UpdateBundles.ps1
	cp $base_dir\Scripts\Raven-GetBundles.ps1 $build_dir\Output\Raven-GetBundles.ps1
	cp $base_dir\readme.txt $build_dir\Output\readme.txt
	cp $base_dir\Help\Documentation.chm $build_dir\Output\Documentation.chm  -ErrorAction SilentlyContinue
	cp $base_dir\acknowledgments.txt $build_dir\Output\acknowledgments.txt
}

task ZipOutput {
	
	if($env:buildlabel -eq 13)
	{
		return 
	}

	$old = pwd
	cd $build_dir\Output
	
	$file = "$release_dir\$global:uploadCategory-Build-$env:buildlabel.zip"
		
	exec { 
		& $tools_dir\zip.exe -9 -A -r `
			$file `
			EmbeddedClient\*.* `
			Client\*.* `
			Samples\*.* `
			Smuggler\*.* `
			Backup\*.* `
			Client-3.5\*.* `
			Web\*.* `
			Bundles\*.* `
			Web\bin\*.* `
			Server\*.* `
			*.*
	}
	
	cd $old
}

task ResetBuildArtifcats {
	git checkout "Raven.Database\RavenDB.snk"
}


task DoRelease -depends Compile, `
	CleanOutputDirectory, `
	CreateOutpuDirectories, `
	CopyEmbeddedClient, `
	CopySmuggler, `
	CopyBackup, `
	CopyClient, `
	CopySilverlight, `
	CopySilverlight-4, `
	CopyClient35, `
	CopyWeb, `
	CopyBundles, `
	CopyServer, `
	CopyRootFiles, `
	CopySamples, `
	ZipOutput, `
	CreateNugetPackage, `
	CreateNugetPackageFineGrained, `
	ResetBuildArtifcats {	
	Write-Host "Done building RavenDB"
}


task Upload -depends DoRelease {
	Write-Host "Starting upload"
	if (Test-Path $uploader) {
		$log = $env:push_msg 
		if(($log -eq $null) -or ($log.Length -eq 0)) {
		  $log = git log -n 1 --oneline		
		}
		
		$log = $log.Replace('"','''') # avoid problems because of " escaping the output
		
		$file = "$release_dir\$global:uploadCategory-Build-$env:buildlabel.zip"
		write-host "Executing: $uploader '$global:uploadCategory' $file ""$log"""
		&$uploader "$uploadCategory" $file "$log"
			
		if ($lastExitCode -ne 0) {
			write-host "Failed to upload to S3: $lastExitCode"
			throw "Error: Failed to publish build"
		}
	}
	else {
		Write-Host "could not find upload script $uploadScript, skipping upload"
	}
	
	
}	

task UploadOpenSource -depends OpenSource, DoRelease, Upload	

task UploadUnstable -depends Unstable, DoRelease, Upload

task CreateNugetPackage {

	Remove-Item $base_dir\RavenDB*.nupkg
	Remove-Item $build_dir\NuPack -Force -Recurse -ErrorAction SilentlyContinue
	New-Item $build_dir\NuPack -Type directory | Out-Null
	New-Item $build_dir\NuPack\content -Type directory | Out-Null
	New-Item $build_dir\NuPack\lib -Type directory | Out-Null
	New-Item $build_dir\NuPack\lib\net35 -Type directory | Out-Null
	New-Item $build_dir\NuPack\lib\net40 -Type directory | Out-Null
	New-Item $build_dir\NuPack\lib\sl40 -Type directory | Out-Null
	New-Item $build_dir\NuPack\tools -Type directory | Out-Null
	New-Item $build_dir\NuPack\server -Type directory | Out-Null

	Remove-Item $build_dir\NuPack-Client -Force -Recurse -ErrorAction SilentlyContinue
	New-Item $build_dir\NuPack-Client -Type directory | Out-Null
	New-Item $build_dir\NuPack-Client\content -Type directory | Out-Null
	New-Item $build_dir\NuPack-Client\lib -Type directory | Out-Null
	New-Item $build_dir\NuPack-Client\lib\net35 -Type directory | Out-Null
	New-Item $build_dir\NuPack-Client\lib\net40 -Type directory | Out-Null
	New-Item $build_dir\NuPack-Client\lib\sl40 -Type directory | Out-Null
	New-Item $build_dir\NuPack-Client\lib\sl50 -Type directory | Out-Null
	New-Item $build_dir\NuPack-Client\tools -Type directory | Out-Null
	
	# package for RavenDB embedded is separate and requires .NET 4.0
	Remove-Item $build_dir\NuPack-Embedded -Force -Recurse -ErrorAction SilentlyContinue
	New-Item $build_dir\NuPack-Embedded -Type directory | Out-Null
	New-Item $build_dir\NuPack-Embedded\content -Type directory | Out-Null
	New-Item $build_dir\NuPack-Embedded\lib -Type directory | Out-Null
	New-Item $build_dir\NuPack-Embedded\lib\net40 -Type directory | Out-Null
	New-Item $build_dir\NuPack-Embedded\tools -Type directory | Out-Null
	
	$client_dlls_3_5 | ForEach-Object { 
		Copy-Item "$_" $build_dir\NuPack\lib\net35
		Copy-Item "$_" $build_dir\NuPack-Client\lib\net35
	}
	$client_dlls | ForEach-Object { 
		Copy-Item "$_" $build_dir\NuPack\lib\net40
		Copy-Item "$_" $build_dir\NuPack-Client\lib\net40
	}
	$silverlight4_dlls | ForEach-Object { 
		Copy-Item "$_" $build_dir\NuPack\lib\sl40
		Copy-Item "$_" $build_dir\NuPack-Client\lib\sl40
	}
	$silverlight_dlls | ForEach-Object { 
		Copy-Item "$_" $build_dir\NuPack\lib\sl50
		Copy-Item "$_" $build_dir\NuPack-Client\lib\sl50
	}
	
	$all_client_dlls | ForEach-Object { 
		Copy-Item "$_" $build_dir\NuPack-Embedded\lib\net40
	}

	# Remove files that are obtained as dependencies
	Remove-Item $build_dir\NuPack\lib\*\Newtonsoft.Json.* -Recurse
	Remove-Item $build_dir\NuPack\lib\*\NLog.* -Recurse
	Remove-Item $build_dir\NuPack-Client\lib\*\Newtonsoft.Json.* -Recurse
	Remove-Item $build_dir\NuPack-Client\lib\*\NLog.* -Recurse
	Remove-Item $build_dir\NuPack-Embedded\lib\*\Newtonsoft.Json.* -Recurse
	Remove-Item $build_dir\NuPack-Embedded\lib\*\NLog.* -Recurse

	# The Server folder is used as a tool, and therefore needs the dependency DLLs in it (can't depend on Nuget for that)
	$server_files | ForEach-Object { Copy-Item "$_" $build_dir\NuPack\server }
	Copy-Item $base_dir\DefaultConfigs\RavenDb.exe.config $build_dir\NuPack\server\Raven.Server.exe.config

	Copy-Item $base_dir\DefaultConfigs\Nupack.Web.config $build_dir\NuPack\content\Web.config.transform
	Copy-Item $base_dir\DefaultConfigs\Nupack.Web.config $build_dir\NuPack-Client\content\Web.config.transform
	Copy-Item $base_dir\DefaultConfigs\Nupack.Web.config $build_dir\NuPack-Embedded\content\Web.config.transform

	Copy-Item $build_dir\Raven.Smuggler.??? $build_dir\NuPack\Tools
	Copy-Item $build_dir\Raven.Smuggler.??? $build_dir\NuPack-Client\Tools
	Copy-Item $build_dir\Raven.Smuggler.??? $build_dir\NuPack-Embedded\Tools

	Copy-Item $build_dir\Raven.Backup.??? $build_dir\NuPack\Tools
	Copy-Item $build_dir\Raven.Backup.??? $build_dir\NuPack-Client\Tools
	Copy-Item $build_dir\Raven.Backup.??? $build_dir\NuPack-Embedded\Tools

	# Generate the .nupkg files
	$nupack = [xml](Get-Content $base_dir\RavenDB.nuspec)
	
	$nugetVersion = "$version.$env:buildlabel"
	if ($global:uploadCategory -and $global:uploadCategory.EndsWith("-Unstable")){
		$nugetVersion += "-Unstable"
	}
	$nupack.package.metadata.version = $nugetVersion

	$writerSettings = new-object System.Xml.XmlWriterSettings
	$writerSettings.Indent = $true
	
	$nupack.Save("$build_dir\Nupack\RavenDB.nuspec");
	&"$tools_dir\nuget.exe" pack $build_dir\NuPack\RavenDB.nuspec

	$tags = $nupack.package.metadata.tags
	
	$nupack.package.metadata.id = "RavenDB-Client"
	$nupack.package.metadata.title = "RavenDB (Client)"
	$nupack.package.metadata.tags = "$tags client"
	$nupack.Save("$build_dir\Nupack-Client\RavenDB-Client.nuspec");
	&"$tools_dir\nuget.exe" pack $build_dir\NuPack-Client\RavenDB-Client.nuspec
	
	$nupack.package.metadata.id = "RavenDB-Embedded"
	$nupack.package.metadata.title = "RavenDB (Embedded)"
	$nupack.package.metadata.tags = "$tags embedded"
	$nupack.Save("$build_dir\Nupack-Embedded\RavenDB-Embedded.nuspec");
	&"$tools_dir\nuget.exe" pack $build_dir\NuPack-Embedded\RavenDB-Embedded.nuspec

	# Upload packages
	$accessPath = "$base_dir\..\Nuget-Access-Key.txt"
	if ( (Test-Path $accessPath) ) {
		$accessKey = Get-Content $accessPath
		$accessKey = $accessKey.Trim()
		
		# Push to nuget repository
		&"$tools_dir\NuGet.exe" push "RavenDB.$nugetVersion.nupkg" $accessKey
		&"$tools_dir\NuGet.exe" push "RavenDB-Client.$nugetVersion.nupkg" $accessKey
		&"$tools_dir\NuGet.exe" push "RavenDB-Embedded.$nugetVersion.nupkg" $accessKey
	}
	else {
		Write-Host "Nuget-Access-Key.txt does not exit. Cannot publish the nuget package." -ForegroundColor Yellow
	}
}

task CreateNugetPackageFineGrained {

	Remove-Item $base_dir\RavenDB*.nupkg
	
	$nuget_dir = "$build_dir\NuGet"
	Remove-Item $nuget_dir -Force -Recurse -ErrorAction SilentlyContinue
	New-Item $nuget_dir -Type directory | Out-Null
	
	New-Item $nuget_dir\RavenDB.Client -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Client\lib -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Client\lib\net35 -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Client\lib\net40 -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Client\lib\sl40 -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Client\lib\sl50 -Type directory | Out-Null
	Copy-Item $base_dir\NuGet\RavenDB.Client.nuspec $nuget_dir\RavenDB.Client\RavenDB.Client.nuspec
	@("Raven.Abstractions-3.5.???", "Raven.Client.Lightweight-3.5.???") |
		ForEach-Object {
			Copy-Item "$build_dir\$_" $nuget_dir\RavenDB.Client\lib\net35
		}
	@("Raven.Abstractions.???", "Raven.Client.Lightweight.???") |
		ForEach-Object {
			Copy-Item "$build_dir\$_" $nuget_dir\RavenDB.Client\lib\net40
		}
	@("Raven.Client.Silverlight-4.???", "AsyncCtpLibrary_Silverlight.???") |
		ForEach-Object {
			Copy-Item "$build_dir\$_" $nuget_dir\RavenDB.Client\lib\sl40
		}
	@("Raven.Client.Silverlight.???", "AsyncCtpLibrary_Silverlight5.???") |
		ForEach-Object {
			Copy-Item "$build_dir\$_" $nuget_dir\RavenDB.Client\lib\sl50
		}
		
	New-Item $nuget_dir\RavenDB.Client.FSharp -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Client.FSharp\lib -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Client.FSharp\lib\net40 -Type directory | Out-Null
	Copy-Item $base_dir\NuGet\RavenDB.Client.FSharp.nuspec $nuget_dir\RavenDB.Client.FSharp\RavenDB.Client.FSharp.nuspec
	@("Raven.Client.Lightweight.FSharp.???") |
		ForEach-Object {
			Copy-Item "$build_dir\$_" $nuget_dir\RavenDB.Client.FSharp\lib\net40
		}
	
	New-Item $nuget_dir\RavenDB.Client.Debug -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Client.Debug\lib -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Client.Debug\lib\net40 -Type directory | Out-Null
	Copy-Item $base_dir\NuGet\RavenDB.Client.Debug.nuspec $nuget_dir\RavenDB.Client.Debug\RavenDB.Client.Debug.nuspec
	@("Raven.Client.Debug.???") |
		ForEach-Object {
			Copy-Item "$build_dir\$_" $nuget_dir\RavenDB.Client.Debug\lib\net40
		}
	
	New-Item $nuget_dir\RavenDB.Client.MvcIntegration -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Client.MvcIntegration\lib -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Client.MvcIntegration\lib\net40 -Type directory | Out-Null
	Copy-Item $base_dir\NuGet\RavenDB.Client.MvcIntegration.nuspec $nuget_dir\RavenDB.Client.MvcIntegration\RavenDB.Client.MvcIntegration.nuspec
	@("Raven.Client.MvcIntegration.???") |
		ForEach-Object {
			Copy-Item "$build_dir\$_" $nuget_dir\RavenDB.Client.MvcIntegration\lib\net40
		}
		
	New-Item $nuget_dir\RavenDB.Database -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Database\lib -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Database\lib\net40 -Type directory | Out-Null
	Copy-Item $base_dir\NuGet\RavenDB.Database.nuspec $nuget_dir\RavenDB.Database\RavenDB.Database.nuspec
	@("Raven.Abstractions.???", "Raven.Database.???", "BouncyCastle.Crypto.???",
			  "Esent.Interop.???", "ICSharpCode.NRefactory.???", "Lucene.Net.???", "Lucene.Net.Contrib.Spatial.???",
			  "Lucene.Net.Contrib.SpellChecker.???", "Raven.Storage.Esent.???", "Raven.Storage.Managed.???", "Raven.Munin.???", "Raven.Studio.xap"  ) |
		ForEach-Object { 
			Copy-Item "$build_dir\$_" $nuget_dir\RavenDB.Database\lib\net40
		}
		
	New-Item $nuget_dir\RavenDB.Embedded -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Embedded\lib -Type directory | Out-Null
	New-Item $nuget_dir\RavenDB.Embedded\lib\net40 -Type directory | Out-Null
	Copy-Item $base_dir\NuGet\RavenDB.Embedded.nuspec $nuget_dir\RavenDB.Embedded\RavenDB.Embedded.nuspec
	@("Raven.Client.Embedded.???") |
		ForEach-Object { 
			Copy-Item "$build_dir\$_" $nuget_dir\RavenDB.Embedded\lib\net40
		}
	
	$nugetVersion = "$version.$env:buildlabel"
	if ($global:uploadCategory -and $global:uploadCategory.EndsWith("-Unstable")){
		$nugetVersion += "-Unstable"
	}
	
	# Sets the package version in all the nuspec as well as any RavenDB package dependency versions
	$packages = Get-ChildItem $nuget_dir *.nuspec -recurse
	$packages | ForEach-Object { 
		$nuspec = [xml](Get-Content $_.FullName)
		$nuspec.package.metadata.version = $nugetVersion
		$nuspec | Select-Xml '//dependency' | ForEach-Object {
			if($_.Node.Id.StartsWith('RavenDB')){
				$_.Node.Version = "[$nugetVersion]"
			}
		}
		$nuspec.Save($_.FullName);
		&"$tools_dir\nuget.exe" pack $_.FullName
	}
	
	# Upload packages
	$accessPath = "$base_dir\..\Nuget-Access-Key.txt"
	if ( (Test-Path $accessPath) ) {
		$accessKey = Get-Content $accessPath
		$accessKey = $accessKey.Trim()
		
		# Push to nuget repository
		$packages | ForEach-Object {
			&"$tools_dir\NuGet.exe" push "$($_.BaseName).$nugetVersion.nupkg" $accessKey
		}
	}
	else {
		Write-Host "Nuget-Access-Key.txt does not exit. Cannot publish the nuget package." -ForegroundColor Yellow
	}
}