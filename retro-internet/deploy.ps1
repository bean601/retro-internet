# Set variables
$remoteUser = "user"
$remoteHost = "local"
$remotePath = "/home/[user]/waybackproxy"
$localPath = ".\retro-internet\bin\Debug\net6.0\*" # Adjust to your actual local path

# Step 1: Define SCP command and copy the files to the remote path
$scpCommand = "scp -r ${localPath} ${remoteUser}@${remoteHost}:${remotePath}"
Write-Host "Step 1: Copying files to the Raspberry Pi..."
Write-Host "Running: $scpCommand"
Invoke-Expression $scpCommand
Write-Host "Files copied successfully."

$stopAppCommand = "ssh ${remoteUser}@${remoteHost} 'sudo pkill -f ""WaybackProxy""'"
Write-Host "Step 3: Stopping any running instance of the application..."
Write-Host "Running: $stopAppCommand"
Invoke-Expression $stopAppCommand
Write-Host "Any running instances stopped."

$remoteCommand = "cd $remotePath && sudo ./WaybackProxy --urls 'http://*:5000'"
$runAppCommand = "ssh ${remoteUser}@${remoteHost} '$remoteCommand'"
Write-Host "Step 4: Running the application on the Raspberry Pi..."
Write-Host "Running: $runAppCommand"
Invoke-Expression $runAppCommand
Write-Host "Application started."


Write-Host "Deployment and execution completed successfully."
