$basedir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$OctoCli = Join-Path $basedir 'octo-cli\octo-cli.exe'

# Define data source and database
$ds = "(local)\<define instance here>"
$db = "<define database here>"

# Create database, import custom ck model and basic rt model
& $OctoCli -c delete -ds $ds -db $db
& $OctoCli -c create -ds $ds -db $db
& $OctoCli -c importck -ds $ds -db $db -f "ConstructionKitModel.json"
& $OctoCli -c importck -ds $ds -db $db -f "RuntimeModel.json"
