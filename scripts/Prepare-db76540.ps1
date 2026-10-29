[CmdletBinding()]
param(
    [ValidateSet('all', 'migrate', 'first-admin', 'prepare-cutover', 'complete-cutover', 'verify')]
    [string]$Step = 'all',
    [string]$SqlServer = 'db76540.public.databaseasp.net',
    [string]$OperationsPath = (Join-Path $PSScriptRoot 'operations/ERPFlow.Operations.dll'),
    [string]$StateDirectory = (Join-Path $PSScriptRoot 'db76540-setup')
)

$ErrorActionPreference = 'Stop'
$database = 'db76540'
if (!(Test-Path -LiteralPath $OperationsPath -PathType Leaf)) { throw 'Set OperationsPath to the published ERPFlow.Operations.dll.' }
if ($SqlServer -notmatch '^db[0-9]+\.public\.databaseasp\.net(,[0-9]+)?$') { throw 'Use the public SQL server name copied from MonsterASP Remote access.' }
if (!(Test-Path -LiteralPath $StateDirectory)) { [IO.Directory]::CreateDirectory($StateDirectory) | Out-Null }
$administratorFile = Join-Path $StateDirectory 'administrator-id.txt'
$preparationFile = Join-Path $StateDirectory 'preparation.json'
$requestFile = Join-Path $StateDirectory 'approved-cutover.json'
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$credential = Get-Credential -UserName 'db76540' -Message 'MonsterASP SQL login from Databases > db76540 > Users (password is not printed or saved)'
if ($null -eq $credential) { throw 'SQL credentials were not supplied.' }
$connection['Data Source'] = $SqlServer
$connection['Initial Catalog'] = $database
$connection['User ID'] = $credential.UserName
$connection['Password'] = $credential.GetNetworkCredential().Password
$connection['Encrypt'] = 'True'
$connection['TrustServerCertificate'] = 'True'
$connection['MultipleActiveResultSets'] = 'True'
$connection['Connect Timeout'] = '30'

function Invoke-Operation([string]$Command, [string[]]$Extra = @()) {
    $output = & dotnet $OperationsPath $Command --confirm-database $database @Extra
    if ($LASTEXITCODE -ne 0) { throw "$Command failed. Stop here; fix the reported input/permission problem before retrying that step." }
    return ($output -join [Environment]::NewLine)
}

try {
    $env:ERPFLOW_OPERATIONS_CONNECTION = $connection.ConnectionString
    $env:ERPFLOW_OPERATIONS_APPROVAL = 'BACKUP-VERIFIED-WRITERS-STOPPED'
    $steps = if ($Step -eq 'all') { @('migrate', 'first-admin', 'prepare-cutover', 'complete-cutover', 'verify') } else { @($Step) }
    foreach ($command in $steps) {
        switch ($command) {
            'migrate' {
                if ((Read-Host 'Confirm the site is offline, recovery is available, and this is the intended empty database. Type db76540') -cne $database) { throw 'Database confirmation did not match.' }
                Invoke-Operation 'migrate'
            }
            'first-admin' {
                $env:ERPFLOW_INITIAL_ADMIN_NAME = Read-Host 'New ERPFlow Administrator display name'
                $admin = Get-Credential -Message 'New ERPFlow Administrator: enter EMAIL as username and a unique password (16-256 characters)'
                if ($null -eq $admin) { throw 'Administrator inputs were not supplied.' }
                try {
                    $env:ERPFLOW_INITIAL_ADMIN_EMAIL = $admin.UserName
                    $env:ERPFLOW_INITIAL_ADMIN_PASSWORD = $admin.GetNetworkCredential().Password
                    $result = Invoke-Operation 'first-admin'
                    if ($result -notmatch 'User ID: ([0-9a-fA-F-]{36})') { throw 'Administrator ID was not returned; do not rerun first-admin blindly.' }
                    $administratorId = [guid]::Parse($Matches[1]).ToString()
                    [IO.File]::WriteAllText($administratorFile, $administratorId)
                    'First Administrator created; ID saved locally. Store the chosen login in your password manager.'
                }
                finally {
                    $env:ERPFLOW_INITIAL_ADMIN_PASSWORD = $null
                    $env:ERPFLOW_INITIAL_ADMIN_EMAIL = $null
                    $env:ERPFLOW_INITIAL_ADMIN_NAME = $null
                    $admin.Password.Dispose()
                    $admin = $null
                }
            }
            'prepare-cutover' {
                $env:ERPFLOW_OPERATOR_USER_ID = [guid]::Parse([IO.File]::ReadAllText($administratorFile).Trim()).ToString()
                $preparation = (Invoke-Operation 'prepare-cutover') | ConvertFrom-Json
                if ($preparation.ProductCount -ne 0 -or $preparation.SalesReservationCount -ne 0 -or $preparation.JobcardItemCount -ne 0) {
                    throw 'Database is not empty. Stop and review its inventory before approving cutover.'
                }
                [IO.File]::WriteAllText($preparationFile, ($preparation | ConvertTo-Json))
                'Empty-database preparation saved locally: 0 products, 0 sales reservations, 0 service items.'
            }
            'complete-cutover' {
                $env:ERPFLOW_OPERATOR_USER_ID = [guid]::Parse([IO.File]::ReadAllText($administratorFile).Trim()).ToString()
                $preparation = [IO.File]::ReadAllText($preparationFile) | ConvertFrom-Json
                if ($preparation.ProductCount -ne 0 -or $preparation.SalesReservationCount -ne 0 -or $preparation.JobcardItemCount -ne 0) { throw 'Preparation is not for an empty database.' }
                $request = [ordered]@{
                    PreparationId = [guid]::Parse($preparation.PreparationId).ToString()
                    ExpectedFingerprint = $preparation.SourceFingerprint
                    DefaultWarehouseId = '0f96d350-19ce-4d10-92d9-b1f06be839f1'
                    PhysicalAttributionApproval = 'Initial empty db76540 demo: no legacy stock or reservations.'
                    ApplicationWriterVersion = 2
                }
                $request | ConvertTo-Json
                if ((Read-Host 'Review this empty-inventory approval. Type db76540 to complete Warehouse activation') -cne $database) { throw 'Cutover was not approved.' }
                [IO.File]::WriteAllText($requestFile, ($request | ConvertTo-Json))
                Invoke-Operation 'complete-cutover' @($requestFile)
            }
            'verify' { Invoke-Operation 'verify' }
        }
    }
}
finally {
    foreach ($name in @('ERPFLOW_OPERATIONS_CONNECTION', 'ERPFLOW_OPERATIONS_APPROVAL', 'ERPFLOW_OPERATOR_USER_ID',
        'ERPFLOW_INITIAL_ADMIN_NAME', 'ERPFLOW_INITIAL_ADMIN_EMAIL', 'ERPFLOW_INITIAL_ADMIN_PASSWORD')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
    $connection.Clear()
    $credential.Password.Dispose()
    $credential = $null
}
