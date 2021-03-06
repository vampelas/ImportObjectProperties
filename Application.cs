using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Connectivity.Explorer.ExtensibilityTools;
using Autodesk.Connectivity.WebServices;
using Autodesk.Connectivity.WebServicesTools;
using VDF = Autodesk.DataManagement.Client.Framework;
using AWS = Autodesk.Connectivity.WebServices;
using log4net;
using log4net.Config;
using Serilog;
using Serilog.Sinks.File;
using Serilog.Core;
using Serilog.Sinks.File.Header;
using Serilog.Sinks.SystemConsole.Themes;

using Microsoft.Extensions.Logging;

namespace ImportObjectProperties
{
    enum MessageCategory
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    class ItemIdComparer : IEqualityComparer<Item>
    {

        public bool Equals(Item first, Item second)
        {
            if (first.Id == second.Id)
            {
                return true;
            }
            return false;
        }

        public int GetHashCode(Item item)
        {
            return item.Id.GetHashCode();
        }
    }

    class Application
    {

        #region Fields

        private const string kFileNameTag = "Name";
        private const string kItemNumberTag = "Number";
        private const string kEntityDefinitionTag = "Custom Entity Definition";
        private const string kEntityNameTag = "Custom Entity Name";
        private const string CATEGORY = "Category";
        private const string LIFECYCLEDEF = "LifeCycleDefinition";
        private const string LIFECYCLESTATE = "LifeCycleState";
        private const string REVISIONSCHEME = "RevisionScheme";
        private const string REVISION = "Revision";
        private List<string> reservedColumns = new List<string>();


        #endregion

        private ApplicationOptions Options { get; set; }
        private List<PropDef> PropertyDefinitions { get; set; }
        private Folder RootFolder { get; set; }
        private WebServiceManager ServiceManager { get; set; }
        private VDF.Vault.Currency.Connections.Connection Connection { get; set; }
        private IExplorerUtil ExplorerUtil { get; set; }

        private PropDef TitleProperty { get; set; }
        private PropDef DescriptionProperty { get; set; }
        private PropDef UnitsProperty { get; set; }

        private Cat[] FileCategories { get; set; }
        private Cat[] EntityCategories { get; set; }
        private LfCycDef[] LfCycDefs { get; set; }
        private RevDefInfo RevDefInformation { get; set; }
        private List<PropDefInfo> PropertyDefinitionInfos { get; set; }

        private static readonly log4net.ILog log = log4net.LogManager.GetLogger("Default");

        public static readonly LoggingLevelSwitch _loggingLevelSwitch = ApplicationOptions.loggingLevelSwitch;

        private static readonly ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            Func<string> headerFactory = () => "Time,Thread,Level,Message,Exception";

            LoggerConfiguration loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(_loggingLevelSwitch)
                //.MinimumLevel.Debug() // <- Set the minimum level
                .WriteTo.Console(theme: AnsiConsoleTheme.Code, outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss} [{ThreadId}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .Enrich.With<ExceptionEnricher>()
                .Enrich.WithThreadId()
                .WriteTo.File(@"_Logs\ImportObjectProperties-.log", retainedFileCountLimit: 10, encoding: Encoding.UTF8,
                rollingInterval: RollingInterval.Day, outputTemplate:
                    "{Timestamp:HH:mm:ss} [{ThreadId}] [{Level:u3}] {Message:lj} {EscapedException}{NewLine}")
                .WriteTo.File(@"_Logs\ImportObjectProperties-.csv", retainedFileCountLimit: 10, encoding: Encoding.UTF8, hooks: new HeaderWriter(headerFactory),
                    rollingInterval: RollingInterval.Day, outputTemplate:
                    "\"{Timestamp:yyyy-MM-dd HH:mm:ss}\",\"[{ThreadId}]\",\"[{Level:u3}]\",\"{Message:lj}\",\"{EscapedException}\"{NewLine}");

            builder.AddSerilog(loggerConfiguration.CreateLogger());
        });

        private static readonly ILogger<Application> SeriLog = loggerFactory.CreateLogger<Application>();

        //public void Format(LogEvent logEvent, IO.TextWriter output)
        //{
        //    output.Write(logEvent.Timestamp.ToString("dd-MM-yyyy H:mm"));
        //    output.Write(",");
        //    output.Write(logEvent.Level);
        //    output.Write(",");
        //    output.Write(logEvent.Properties["KEY"]);
        //    output.Write(",");
        //    //...
        //    output.WriteLine();
        //}

        public static void PrintHeader()
        {
            SeriLog.LogInformation($"ImportObjectProperties v{Assembly.GetExecutingAssembly().GetName().Version.ToString()} - imports property values from a CSV file");
            SeriLog.LogInformation("Copyright (c) 2021 Autodesk, Inc. All rights reserved.");
            SeriLog.LogInformation("Modified By: Vas Ampelas");
            SeriLog.LogInformation("Uses Serilog logger to produce logs for Console, Text, and CSV files.");
            SeriLog.LogInformation("https://github.com/vampelas/ImportObjectProperties.git");
            SeriLog.LogInformation("");

            //Console.WriteLine("ImportObjectProperties v{0} - imports property values from a CSV file",
            //    Assembly.GetExecutingAssembly().GetName().Version.ToString());
            //Console.WriteLine("Copyright (c) 2018 Autodesk, Inc. All rights reserved.");
            //Console.WriteLine("");
        }

        public static void PrintHelp()
        {
            SeriLog.LogInformation("Usage: ImportObjectProperties:");
            SeriLog.LogInformation("1. Run Command Prompt Windows (As Administrator)...");
            SeriLog.LogInformation("2. Navigate to ImportObjectProperties Install Dir.");
            SeriLog.LogInformation("[-t File|Item|CustEnt] [-a Vault|Windows] [-s srv] [-db dbase] [-u user] [-p pwd] [-l d] filename [-e codepage|name]");
            SeriLog.LogInformation("  -t                Type of object whose properties are being imported (default = File).");
            SeriLog.LogInformation("  -a                Authentication type (default = Vault).");
            SeriLog.LogInformation("  -s                Name of server (default = localhost).");
            SeriLog.LogInformation("  -db               Name of database (default = Vault).");
            SeriLog.LogInformation("  -u                UserName for access to database (default = Administrator).");
            SeriLog.LogInformation("  -p                Password for access to database (default = empty password).");
            SeriLog.LogInformation("  -l                Logging level (d = Debug).");
            SeriLog.LogInformation("  filename          CSV File which contains property values.");
            SeriLog.LogInformation("  -e                Encoding. Provide either codepage or name. (default = UTF-8).");

            //Console.WriteLine("Usage: ImportObjectProperties [-t File|Item|CustEnt] [-a Vault|Windows] [-s srv] [-db dbase] [-u user] [-p pwd] [-l d] filename [-e codepage|name]");
            //Console.WriteLine("  -t                Type of object whose properties are being imported (default = File).");
            //Console.WriteLine("  -a                Authentication type (default = Vault).");
            //Console.WriteLine("  -s                Name of server (default = localhost).");
            //Console.WriteLine("  -db               Name of database (default = Vault).");
            //Console.WriteLine("  -u                UserName for access to database (default = Administrator).");
            //Console.WriteLine("  -p                Password for access to database (default = empty password).");
            //Console.WriteLine("  -l                Logging level (d = Debug).");
            //Console.WriteLine("  -e                Encoding. Provide either codepage or name. (default = UTF-8).");
            //Console.WriteLine("  filename          CSV File which contains property values.");
        }



        public void Run(ApplicationOptions options)
        {

            Options = options;
            VDF.Vault.Currency.Connections.AuthenticationFlags authFlags = VDF.Vault.Currency.Connections.AuthenticationFlags.Standard;
            


            if (Options.AuthenticationType == AuthTyp.ActiveDir)
            {
                authFlags = VDF.Vault.Currency.Connections.AuthenticationFlags.WindowsAuthentication;
            }
            VDF.Vault.Results.LogInResult result =
                VDF.Vault.Library.ConnectionManager.LogIn(Options.Server, Options.KnowledgeVault, Options.UserName, Options.Password, authFlags, null);

            if (result.Success == false)
            {
                string message = "Login failed";
                if (result.Exception == null)
                {
                    if (result.ErrorMessages.Count > 0)
                    {
                        message = result.ErrorMessages.ElementAt(0).Key.ToString() + ", " + result.ErrorMessages.ElementAt(0).Value;
                    }
                }
                else
                {
                    message = VDF.Library.ExceptionParser.GetMessage(result.Exception);
                }
                SeriLog.LogError(new Exception("Error connecting to Vault: "), message);
                //log.Error($"Error connecting to Vault: {message}");
                //Log(MessageCategory.Error, "Error connecting to Vault: {0}", message);
                return;
            }
            try
            {
                Connection = result.Connection;
                ServiceManager = result.Connection.WebServiceManager;
                ExplorerUtil = ExplorerLoader.LoadExplorerUtil(Options.Server, Options.KnowledgeVault, ServiceManager.WebServiceCredentials.SecurityHeader.UserId, ServiceManager.WebServiceCredentials.SecurityHeader.Ticket);
                DataTable table = ReadData(options.InputFile);

                FileCategories = ServiceManager.CategoryService.GetCategoriesByEntityClassId("FILE", true);
                EntityCategories = ServiceManager.CategoryService.GetCategoriesByEntityClassId("CUSTENT", true);
                LfCycDefs = ServiceManager.LifeCycleService.GetAllLifeCycleDefinitions();
                RevDefInformation = ServiceManager.RevisionService.GetAllRevisionDefinitionInfo();

                reservedColumns.Add(CATEGORY);
                reservedColumns.Add(LIFECYCLEDEF);
                reservedColumns.Add(LIFECYCLESTATE);
                reservedColumns.Add(REVISION);
                reservedColumns.Add(REVISIONSCHEME);

                string filename = Options.InputFile;

                SeriLog.LogInformation($"Input file: '{filename}'");
                SeriLog.LogInformation($"  Number of rows: '{table.Rows.Count}'");
                //SeriLog.LogInformation("");
                //Log(MessageCategory.Info, "Input file: '{0}'", filename);
                //Log(MessageCategory.Info, "  Number of rows: '{0}'", table.Rows.Count);
                //Log(MessageCategory.Info, "");

                switch (Options.ObjectType)
                {
                    case ImportType.File:
                        RunFileImport(table);
                        break;

                    case ImportType.Item:
                        RunItemImport(table);
                        break;

                    case ImportType.CustomEntity:
                        RunCustomEntityImport(table);
                        break;

                    default:
                        SeriLog.LogError("Invalid value for parameter -t.", "");
                        //log.Error("Invalid value for parameter -t.");
                        //Log(MessageCategory.Error, "ERROR: Invalid value for parameter -t.");
                        break;
                }
            }
            finally
            {
                VDF.Vault.Library.ConnectionManager.CloseAllConnections();
            }
        }

        private void RunFileImport(DataTable table)
        {
            RootFolder = ServiceManager.DocumentService.GetFolderRoot();
            ReadPropertyDefinitions();
            foreach (DataRow row in table.Rows)
            {
                try
                {
                    string fileName = Convert.ToString(row[kFileNameTag]);
                    SeriLog.LogInformation("");
                    SeriLog.LogInformation($"[{table.Rows.IndexOf(row) + 1}/{table.Rows.Count}]: Processing file '{fileName}'...");
                    //Log(MessageCategory.Info, "[{0}/{1}]: Processing file '{2}'...",
                    //    table.Rows.IndexOf(row) + 1, table.Rows.Count, fileName);
                    ProcessFileRow(fileName, row);
                    //SeriLog.LogInformation("");
                    //Log(MessageCategory.Info, "");
                }
                catch (Exception ex)
                {
                    SeriLog.LogDebug(VDF.Library.ExceptionParser.GetMessage(ex), $"{Convert.ToString(row[kFileNameTag])}");
                    SeriLog.LogDebug($"Source: {VDF.Library.ExceptionParser.GetMessage(ex)}");
                    SeriLog.LogDebug($"StackTrace: {ex.Source}");
                    SeriLog.LogDebug($"Target: {ex.TargetSite}");
                    //SeriLog.LogInformation("");
                    //Log(MessageCategory.Error, "ERROR: {0}", VDF.Library.ExceptionParser.GetMessage(ex));
                    //Log(MessageCategory.Debug, " Source: {0}", ex.Source);
                    //Log(MessageCategory.Debug, " StackTrace: {0}", ex.StackTrace);
                    //Log(MessageCategory.Debug, " Target: {0}", ex.TargetSite);
                }
            }
        }

        private void RunItemImport(DataTable table)
        {
            ReadPropertyDefinitions();
            foreach (DataRow row in table.Rows)
            {
                try
                {
                    string itemNumber = Convert.ToString(row[kItemNumberTag]);
                    SeriLog.LogInformation("");
                    SeriLog.LogInformation($"[{table.Rows.IndexOf(row) + 1}/{table.Rows.Count}]: Processing item '{itemNumber}'...");
                    //Log(MessageCategory.Info, "[{0}/{1}]: Processing item '{2}'...", table.Rows.IndexOf(row) + 1, table.Rows.Count, itemNumber);
                    ProcessItemRow(itemNumber, row);
                    //SeriLog.LogInformation("");
                    //Log(MessageCategory.Info, "");
                }
                catch (Exception ex)
                {
                    SeriLog.LogDebug(VDF.Library.ExceptionParser.GetMessage(ex), $"{Convert.ToString(row[kItemNumberTag])}");
                    SeriLog.LogDebug($"Source: {ex.Source}");
                    SeriLog.LogDebug($"StackTrace: {ex.StackTrace}");
                    SeriLog.LogDebug($"Target: {ex.TargetSite}");
                    //SeriLog.LogInformation("");
                    //Log(MessageCategory.Error, "ERROR: {0}", VDF.Library.ExceptionParser.GetMessage(ex));
                    //Log(MessageCategory.Debug, " Source: " + ex.Source);
                    //Log(MessageCategory.Debug, " StackTrace: " + ex.StackTrace);
                    //Log(MessageCategory.Debug, " Target: " + ex.TargetSite);
                }
            }
        }

        private void RunCustomEntityImport(DataTable table)
        {
            ReadPropertyDefinitions();
            foreach (DataRow row in table.Rows)
            {
                try
                {
                    string name = Convert.ToString(row[kEntityNameTag]);
                    SeriLog.LogInformation("");
                    SeriLog.LogInformation($"[{table.Rows.IndexOf(row) + 1}/{table.Rows.Count}]: Processing entity '{name}'...");
                    //Log(MessageCategory.Info, "[{0}/{1}]: Processing entity '{2}'...",
                    //    table.Rows.IndexOf(row) + 1, table.Rows.Count, name);
                    ProcessEntityRow(row);
                    //SeriLog.LogInformation("");
                    //Log(MessageCategory.Info, "");
                }
                catch (Exception ex)
                {
                    SeriLog.LogError(ex.Message, $"{Convert.ToString(row[kEntityNameTag])}");
                    SeriLog.LogDebug($"Source: {ex.Source}");
                    SeriLog.LogDebug($"StackTrace: {ex.StackTrace}");
                    SeriLog.LogDebug($"Target: {ex.TargetSite}");
                    //SeriLog.LogInformation("");
                    //Log(MessageCategory.Error, "ERROR: {0}", ex.Message);
                    //Log(MessageCategory.Debug, " Source: {0}", ex.Source);
                    //Log(MessageCategory.Debug, " StackTrace: {0}", ex.StackTrace);
                    //Log(MessageCategory.Debug, " Target: {0}", ex.TargetSite);
                }
            }
        }

        private DataTable ReadData(string fileName)
        {
            string separator = ConfigurationManager.AppSettings["CSVSeparatorInAscii"];

            if (string.IsNullOrEmpty(separator) == false)
            {
                int code = Convert.ToInt32(separator);

                CSVDataReader.Separator = Convert.ToChar(code);
            }
            EncodingInfo[] encodingInfos = Encoding.GetEncodings();
            EncodingInfo encodingInfo = null;

            if (encodingInfos != null)
            {
                encodingInfo = encodingInfos.FirstOrDefault(e => e.Name.Equals(Options.Encoding, StringComparison.CurrentCultureIgnoreCase));
                if (encodingInfo == null)
                {
                    int codePage;

                    if (Int32.TryParse(Options.Encoding, out codePage))
                    {
                        encodingInfo = encodingInfos.FirstOrDefault(e => e.CodePage == codePage);
                    }
                }
            }
            if (encodingInfo == null)
            {
                SeriLog.LogError("Invalid value for encoding. Either valid code page or encoding name must be provided.");
                //throw new Exception("Invalid value for encoding. Either valid code page or encoding name must be provided.");
            }
            CSVDataReader.Encoding = encodingInfo.GetEncoding();
            DataTable result = CSVDataReader.ReadFile(fileName);

            return result;
        }

        private void ReadPropertyDefinitions()
        {
            PropertyDefinitions = new List<PropDef>();

            switch (Options.ObjectType)
            {
                case ImportType.File:
                    PropertyDefinitions.AddRange(ServiceManager.PropertyService.GetPropertyDefinitionsByEntityClassId("FILE"));
                    break;

                case ImportType.Item:
                    PropertyDefinitions.AddRange(ServiceManager.PropertyService.GetPropertyDefinitionsByEntityClassId("ITEM"));
                    TitleProperty = PropertyDefinitions.FirstOrDefault(p => p.DispName.Equals(Options.TitleProperty, StringComparison.CurrentCultureIgnoreCase));
                    DescriptionProperty = PropertyDefinitions.FirstOrDefault(p => p.DispName.Equals(Options.DescriptionProperty, StringComparison.CurrentCultureIgnoreCase));
                    UnitsProperty = PropertyDefinitions.FirstOrDefault(p => p.DispName.Equals(Options.UnitsProperty, StringComparison.CurrentCultureIgnoreCase));
                    break;

                case ImportType.CustomEntity:
                    PropertyDefinitions.AddRange(ServiceManager.PropertyService.GetPropertyDefinitionsByEntityClassId("CUSTENT"));
                    break;

                default:
                    SeriLog.LogError("Invalid value for parameter -t.");
                    //Log(MessageCategory.Error, "ERROR: Invalid value for parameter -t.");
                    break;
            }
        }

        private void ProcessFileRow(string fileName, DataRow row)
        {
            File file = GetFile(fileName);

            if (file.CheckedOut == true)
            {
                SeriLog.LogError(new Exception("File is checked out"), fileName);
                //SeriLog.LogInformation("");
                //throw new Exception("File is checked out.");
            }
            if (file.Locked == true)
            {
                SeriLog.LogDebug(new Exception("File is locked."), "File is locked.");
                //throw new Exception("File is locked.");
            }
            if (file.Cloaked == true)
            {
                SeriLog.LogDebug(new Exception("Permission denied."), "Permission denied.");
                //throw new Exception("Permission denied.");
            }
            if (Options.UseExplorerUtil)
            {
                UpdatePropertiesUsingExplorerUtil(file, row);
            }
            else
            {
                UpdateFileProperties(file, row);
            }
        }

        private void UpdatePropertiesUsingExplorerUtil(File file, DataRow row)
        {
            File theFile = UpdateFileCategory(file, row);
            theFile = UpdateFileRevision(theFile, row);

            Dictionary<PropDef, object> propertyValues = new Dictionary<PropDef, object>();

            foreach (DataColumn column in row.Table.Columns)
            {
                if (0 == string.Compare(column.ColumnName, kFileNameTag))
                {
                    continue;
                }
                if (reservedColumns.Contains(column.ColumnName, StringComparer.CurrentCultureIgnoreCase))
                {
                    continue;
                }
                PropDef prop = GetProperty(column.ColumnName);

                if (prop == null)
                {
                    //property not found. Warn user and continue processing
                    SeriLog.LogWarning($" Unknown property: {column.ColumnName}");
                    //log.Warn($" Unknown property: {column.ColumnName}");
                    //Log(MessageCategory.Warning, "WARNING:  Unknown property: {0}", column.ColumnName); continue;
                }
                object value = GetPropertyValue(prop, row.Field<string>(column));

                propertyValues.Add(prop, value);
            }
            if (propertyValues.Count > 0)
            {
                try
                {
                    ExplorerUtil.UpdateFileProperties(theFile, propertyValues);
                }
                catch (Exception ex)
                {
                    SeriLog.LogError(new Exception("Error when updating file properties: " + VDF.Library.ExceptionParser.GetMessage(ex)), "");
                    //throw new Exception("Error when updating file properties: " + VDF.Library.ExceptionParser.GetMessage(ex));
                }
                SeriLog.LogInformation($"Successfully Imported properties: {propertyValues.Count}");
                //log.Info($"Imported properties: {propertyValues.Count}");
                //Console.WriteLine("Imported properties: {0}", propertyValues.Count);
            }
            UpdateFileLifeCycleDefinition(theFile, row);
        }

        private PropDefInfo GetPropertyDefinitionInfo(PropertyService svc, string entityClassId, long propDefId)
        {
            if (PropertyDefinitionInfos == null)
            {
                PropertyDefinitionInfos = new List<PropDefInfo>();
            }
            PropDefInfo propDefInfo = PropertyDefinitionInfos.FirstOrDefault(d => d.PropDef.Id == propDefId);

            if (propDefInfo != null)
            {
                return propDefInfo;
            }
            PropDefInfo[] propDefInfos = svc.GetPropertyDefinitionInfosByEntityClassId(entityClassId, new long[] { propDefId });

            propDefInfo = propDefInfos[0];
            PropertyDefinitionInfos.Add(propDefInfo);
            return propDefInfo;
        }

        private IEnumerable<FileAssocParam> GetFileReferences(DocumentService svc, long fileId, 
            FileAssociationTypeEnum associationType =  FileAssociationTypeEnum.All, bool includeHidden = true)
        {
            FileAssocArray[] fileAssocationLists = svc.GetFileAssociationsByIds(new[] { fileId },
                FileAssociationTypeEnum.None,
                false, associationType, false, false,
                includeHidden);

            if (fileAssocationLists == null)
            {
                yield break;
            }
            foreach (FileAssocArray fileAssocationList in fileAssocationLists)
            {
                if (fileAssocationList.FileAssocs == null)
                {
                    continue;
                }
                foreach (FileAssoc fileAssoc in fileAssocationList.FileAssocs)
                {
                    var assocParam = new FileAssocParam
                    {
                        CldFileId = fileAssoc.CldFile.Id,
                        ExpectedVaultPath = fileAssoc.ExpectedVaultPath,
                        RefId = fileAssoc.RefId,
                        Source = fileAssoc.Source,
                        Typ = fileAssoc.Typ,
                    };

                    yield return assocParam;
                }
            }

            yield break;

        }

        private VDF.Vault.Currency.Properties.ContentSourceProvider GetContentSourceProvider(AWS.File file)
        {
            long[] contentIds = Connection.WebServiceManager.DocumentService.GetContentSourceIdsByFileIds(new long[] { file.Id });

            return Connection.ConfigurationManager.GetContentSourceProvider(contentIds[0]);
        }

        private File UpdateFileProperties(File file, DataRow row)
        {
            Autodesk.Connectivity.WebServices.ByteArray fileContents = null;

            File theFile = UpdateFileCategory(file, row);
            theFile = UpdateFileRevision(theFile, row);

            List<long> propIds = new List<long>();
            List<object> propValues = new List<object>();

            #region Collect properties to update
            foreach (DataColumn column in row.Table.Columns)
            {
                if (0 == string.Compare(column.ColumnName, kFileNameTag))
                {
                    continue;
                }
                if (reservedColumns.Contains(column.ColumnName, StringComparer.CurrentCultureIgnoreCase))
                {
                    continue;
                }
                PropDef prop = GetProperty(column.ColumnName);

                if (prop == null)
                {
                    //property not found. Warn user and continue processing
                    SeriLog.LogWarning($" Unknown property: {column.ColumnName}"); continue;
                    //Log(MessageCategory.Warning, "WARNING:  Unknown property: {0}", column.ColumnName); continue;
                }
                object value = GetPropertyValue(prop, row.Field<string>(column));

                propIds.Add(prop.Id);
                propValues.Add(value);
            }
            #endregion

            if (propIds.Count > 0)
            {

                VDF.Vault.Currency.Properties.ContentSourceProvider provider = GetContentSourceProvider(file);
                VDF.Vault.Currency.Properties.PropertyDefinitionDictionary definitions =
                    Connection.PropertyManager.GetPropertyDefinitions(VDF.Vault.Currency.Entities.EntityClassIds.Files, null, VDF.Vault.Currency.Properties.PropertyDefinitionFilter.IncludeAll);
                List<AWS.PropWriteReq> fileProperties = new List<AWS.PropWriteReq>();
                List<AWS.PropInstParam> propertyInstances = new List<AWS.PropInstParam>();
                List<VDF.Vault.Currency.Properties.ContentSourcePropertyMapping> mappingDefinitions = new List<VDF.Vault.Currency.Properties.ContentSourcePropertyMapping>();


                ByteArray[] tickets = ServiceManager.DocumentService.GetDownloadTicketsByFileIds(new long[] { theFile.Id });
                ByteArray ticket = tickets[0];

                #region Separate properties into mapped and unmapped property collections
                for (int i = 0; i < propIds.Count; i++)
                {

                    VDF.Vault.Currency.Properties.PropertyDefinition definition = definitions.Values.FirstOrDefault(d => d.Id == propIds[i]);

                    // ignore inactive and system properties
                    if (definition.Active == false)
                    {
                        continue;
                    }
                    if (definition.IsSystem)
                    {
                        continue;
                    }
                    object propertyValue = propValues[i];
                    // find if this is file property or database property
                    bool isMapped = false;

                    if (definition.Mappings.HasMappings)
                    {
                        IEnumerable<VDF.Vault.Currency.Properties.ContentSourcePropertyMapping> mappings =
                            definition.Mappings.GetContentSourcePropertyMappings(VDF.Vault.Currency.Entities.EntityClassIds.Files, provider.SystemName);

                        // only write mappings
                        foreach (VDF.Vault.Currency.Properties.ContentSourcePropertyMapping mapping in mappings)
                        {
                            if ((mapping.MappingDirection == VDF.Vault.Currency.Properties.MappingDirection.ReadWrite) ||
                                (mapping.MappingDirection == VDF.Vault.Currency.Properties.MappingDirection.WriteToContentSource))
                            {
                                AWS.PropWriteReq req = new AWS.PropWriteReq
                                {
                                    CanCreate = mapping.CreateNew,
                                    Moniker = mapping.ContentPropertyDefinition.Moniker,
                                    Val = propertyValue,
                                };

                                fileProperties.Add(req);
                                mappingDefinitions.Add(mapping);
                                isMapped = true;
                            }
                        }
                    }
                    if (isMapped == false)
                    {
                        AWS.PropInstParam propInst = new AWS.PropInstParam
                        {
                            PropDefId = definition.Id,
                            Val = propertyValue,
                        };

                        propertyInstances.Add(propInst);
                    }
                }
                #endregion

                if ((fileProperties.Count == 0) && (propertyInstances.Count == 0))
                {
                    return theFile;
                }
                IEnumerable<FileAssocParam> fileAssociations = GetFileReferences(ServiceManager.DocumentService, theFile.Id);

                ServiceManager.DocumentService.CheckoutFile(file.Id, CheckoutFileOptions.Master, Environment.MachineName, string.Empty, "Check out for property editing", out fileContents);

                #region Update BOM mapped properties
                BOM bom = ServiceManager.DocumentService.GetBOMByFileId(file.Id); 

                // if there is BOM blob available then we need to update BOM properties
                if (bom != null)
                {
                    BOMDataAdapter adapter = new BOMDataAdapter(bom);

                    adapter.PropertyDefinitions.AddRange(mappingDefinitions);
                    bom = adapter.UpdateProperties(fileProperties);
                }
                #endregion

                ByteArray uploadTicket = null;

                if (fileProperties.Any())
                {
                    PropWriteResults results;

                    uploadTicket = new ByteArray();
                    uploadTicket.Bytes = ServiceManager.FilestoreService.CopyFile(ticket.Bytes, null, true, fileProperties.ToArray(), out results);
                }
                if (propertyInstances.Any())
                {
                    try
                    {
                        AWS.PropInstParamArray propInstArray = new AWS.PropInstParamArray
                        {
                            Items = propertyInstances.ToArray(),
                        };

                        ServiceManager.DocumentService.UpdateFileProperties(new long[] { theFile.MasterId }, new AWS.PropInstParamArray[] { propInstArray });
                    }
                    catch (Exception e)
                    {
                        ServiceManager.DocumentService.UndoCheckoutFile(theFile.MasterId, out fileContents);
                        SeriLog.LogError(VDF.Library.ExceptionParser.GetMessage(e), "Error updating file properties: ");
                        //Console.Write("Error updating file properties: " + VDF.Library.ExceptionParser.GetMessage(e));
                    }
                }

                try
                {
                    theFile = ServiceManager.DocumentService.CheckinUploadedFile(theFile.MasterId, "Update properties", false, DateTime.Now,
                        fileAssociations.ToArray(), bom, bom == null ? true : false, theFile.Name, theFile.FileClass, theFile.Hidden, uploadTicket);
                }
                catch (Exception e)
                {
                    SeriLog.LogError(VDF.Library.ExceptionParser.GetMessage(e), "Error checking in file: ");
                    //Console.Write("Error checking in file: " + VDF.Library.ExceptionParser.GetMessage(e));
                }
                SeriLog.LogInformation($"Imported properties: {propIds.Count}");
                //Console.Write("Imported properties: {0}", propIds.Count);
            }

            UpdateFileLifeCycleDefinition(theFile, row);

            return file;
        }

        private File UpdateFileLifeCycleDefinition(File file, DataRow row)
        {
            File theFile = file;

            if (null == row.Table.Columns[LIFECYCLEDEF])
            {
                return theFile;
            }

            string lfCycDef = row.Field<string>(LIFECYCLEDEF);
            if (string.IsNullOrWhiteSpace(lfCycDef))
            {
                SeriLog.LogDebug("No lifecycle definition in input file - will not update lifecycle");
                //Log(MessageCategory.Debug, "No lifecycle definition in input file - will not update lifecycle");
            }

            string lfCycState = row.Field<string>(LIFECYCLESTATE);
            if (false == string.IsNullOrWhiteSpace(lfCycDef) && false == string.IsNullOrWhiteSpace(lfCycState))
            {
                try
                {
                    LfCycDef def = LfCycDefs.Single(l => l.DispName == lfCycDef);
                    long toStateId = def.StateArray.Single(s => s.DispName.Equals(lfCycState)).Id;

                    if (theFile.FileLfCyc.LfCycDefId != def.Id)
                    {
                        SeriLog.LogInformation($"Updating lifecycle definition to {lfCycDef} and state to {lfCycState}");
                        //Log(MessageCategory.Info, "Updating lifecycle definition to {0} and state to {1}", lfCycDef, lfCycState);
                        long[] masterIds = new long[] { file.MasterId };
                        long[] lfCycDefIds = new long[] { def.Id };
                        long[] toStateIds = new long[] { toStateId };

                        File[] files = ServiceManager.DocumentServiceExtensions.UpdateFileLifeCycleDefinitions(masterIds, lfCycDefIds, toStateIds, "Updated lifecycle");
                        theFile = files[0];
                        SeriLog.LogInformation("Successfully updated lifecycle definition");
                        //Log(MessageCategory.Debug, "Successfully updated lifecycle definition");
                    }
                    else if (theFile.FileLfCyc.LfCycDefId == def.Id && lfCycState != theFile.FileLfCyc.LfCycStateName)
                    {
                        SeriLog.LogInformation($"Updating lifecycle state to {lfCycState}");
                        //Log(MessageCategory.Info, "Updating lifecycle state to {0}", lfCycState);
                        long[] masterIds = new long[] { file.MasterId };
                        long[] toStateIds = new long[] { toStateId };
                        ServiceManager.DocumentServiceExtensions.UpdateFileLifeCycleStates(masterIds, toStateIds, "Updated lifecycle state");
                    }
                    else
                    {
                        SeriLog.LogInformation($"File already has lifecycle definition {lfCycDef} and state {lfCycState}");
                        //Log(MessageCategory.Debug, "File already has lifecycle definition {0} and state {1}", lfCycDef, lfCycState);
                    }
                }
                catch (Exception)
                {
                    SeriLog.LogError($"Could not assign new lifecycle definition {lfCycDef} or state {lfCycState} to file ");
                    //Log(MessageCategory.Error, "Could not assign new lifecycle definition {0} or state {1} to file ", lfCycDef, lfCycState);
                    throw;
                }
            }
            else
            {
                SeriLog.LogInformation($"No values found for {LIFECYCLEDEF} or {LIFECYCLESTATE}. Could not change lifecycle (definition) of the file");
                //Log(MessageCategory.Info, "No values found for {0} or {1}. Could not change lifecycle (definition) of the file", LIFECYCLEDEF, LIFECYCLESTATE);
            }
            return theFile;
        }

        private File UpdateFileCategory(File file, DataRow row)
        {
            File theFile = file;

            if (null == row.Table.Columns[CATEGORY])
            {
                return theFile;
            }

            string category = row.Field<string>(CATEGORY);
            if (string.IsNullOrWhiteSpace(category))
            {
                SeriLog.LogDebug("No category found - will not update category");
                //Log(MessageCategory.Debug, "No category found - will not update category");
                return theFile;
            }

            Cat cat;
            try
            {
                cat = FileCategories.Single(c => c.Name.Equals(category, StringComparison.InvariantCultureIgnoreCase));

                if (theFile.Cat.CatId != cat.Id)
                {
                    long[] categoryIds = new long[] { cat.Id };
                    long[] masterIds = new long[] { file.MasterId };

                    File[] files = ServiceManager.DocumentServiceExtensions.UpdateFileCategories(masterIds, categoryIds, "updated file category to " + category);
                    theFile = files[0];
                    SeriLog.LogInformation($"File category changed to {cat.Name}");
                    //Log(MessageCategory.Info, "File category changed to {0}", cat.Name);
                }
                else
                {
                    SeriLog.LogDebug($"File already has category {category} - no change");
                    //Log(MessageCategory.Debug, "File already has category {0} - no change", category);
                }
            }
            catch (Exception)
            {
                SeriLog.LogInformation($"Could not assign new category {category} to file ");
                //Log(MessageCategory.Info, "Could not assign new category {0} to file ", category);
                throw;
            }

            return theFile;
        }

        private File UpdateFileRevision(File file, DataRow row)
        {
            File theFile = file;

            if (null == row.Table.Columns[REVISIONSCHEME])
            {
                return theFile;
            }

            string revScheme = row.Field<string>(REVISIONSCHEME);
            if (string.IsNullOrWhiteSpace(revScheme))
            {
                SeriLog.LogDebug("No revision scheme found - will not update revision information");
                //Log(MessageCategory.Debug, "No revision scheme found - will not update revision information");
                return theFile;
            }
         
            string revName = row.Field<string>(REVISION);
            if (false == string.IsNullOrWhiteSpace(revScheme) && false == string.IsNullOrWhiteSpace(revName))
            {
                try
                {

                    RevDef[] definitions = RevDefInformation.RevDefArray;
                    RevDef definition = RevDefInformation.RevDefArray.Single(d => d.DispName.Equals(revScheme, StringComparison.InvariantCultureIgnoreCase));

                    if (theFile.FileRev.RevDefId != definition.Id || theFile.FileRev.Label != revName)
                    {
                        SeriLog.LogInformation($"Updating revision to {definition.DispName}, {revName}");
                        //Log(MessageCategory.Info, "Updating revision to {0}, {1}", definition.DispName, revName);
                        long[] fileIds = new long[] { theFile.Id };
                        long[] revDefIds = new long[] { definition.Id };

                        File[] files = ServiceManager.DocumentServiceExtensions.UpdateRevisionDefinitionAndNumbers(fileIds, revDefIds, new string[] { revName }, "Update revision to " + revName);
                        theFile = files[0];
                    }
                    else
                    {
                        SeriLog.LogDebug($"File already has revision definition {definition.DispName} and revision name {revName}");
                        //Log(MessageCategory.Debug, "File already has revision definition {0} and revision name {1}", definition.DispName, revName);
                    }
                }
                catch (Exception)
                {
                    SeriLog.LogError($"Could not assign new revision {revName} or definition {revScheme} to file ");
                    //Log(MessageCategory.Error, "Could not assign new revision {0} or definition {1} to file ", revName, revScheme);
                    throw;
                }
            }
            else
            {
                SeriLog.LogInformation($"No values found for {REVISIONSCHEME} or {REVISION}. Could not change revision (scheme) of the file");
                //Log(MessageCategory.Info, "No values found for {0} or {1}. Could not change revision (scheme) of the file", REVISIONSCHEME, REVISION);
            }
            return theFile;
        }

        private void ProcessItemRow(string itemNumber, DataRow row)
        {
            Item[] editableItems = null;
            try
            {
                Item item = GetItem(itemNumber);

                if (item == null)
                {
                    SeriLog.LogError(new Exception("Item not found."), "");
                    //throw new Exception("Item not found.");
                }
                if (item.IsCloaked == true)
                {
                    SeriLog.LogError(new Exception("Item is not available."), "");
                    //throw new Exception("Item is not available.");
                }

                // read current properties
                IEnumerable<long> propertyIds = from p in PropertyDefinitions
                                                where p.IsSys == false
                                                select p.Id;
                PropInst[] currentProperties =
                    ServiceManager.PropertyService.GetProperties("ITEM", new long[] { item.Id }, propertyIds.ToArray());
                List<PropInst> properties = new List<PropInst>();

                if (currentProperties != null)
                {
                    properties.AddRange(currentProperties);
                }
                // edit an item
                editableItems = ServiceManager.ItemService.EditItems(new long[] { item.RevId });
                List<long> addedPropDefIds = new List<long>();

                List<PropInstParam> propertyValues = new List<PropInstParam>();
                string title = string.Empty,
                    description = string.Empty,
                    units = string.Empty;

                foreach (DataColumn column in row.Table.Columns)
                {
                    if (0 == string.Compare(column.ColumnName, kItemNumberTag))
                    {
                        continue;
                    }
                    PropDef prop = GetProperty(column.ColumnName);

                    if (prop == null)
                    {
                        SeriLog.LogWarning($" Unknown property: {column.ColumnName}");
                        //Log(MessageCategory.Warning, "WARNING:  Unknown property: {0}", column.ColumnName);
                        continue;
                    }
                    object value = GetPropertyValue(prop, row.Field<string>(column));

                    if (prop.IsSys)
                    {
                        if ((TitleProperty != null) && (prop.Id == TitleProperty.Id))
                        {
                            title = Convert.ToString(value);
                        }
                        else if ((DescriptionProperty != null) && (prop.Id == DescriptionProperty.Id))
                        {
                            description = Convert.ToString(value);
                        }
                        else if ((UnitsProperty != null) && (prop.Id == UnitsProperty.Id))
                        {
                            units = Convert.ToString(value);
                        }
                    }
                    else
                    {
                        if (properties.Any(p => p.PropDefId == prop.Id) == false)
                        {
                            // item doesn't have property - we need to add it
                            addedPropDefIds.Add(prop.Id);
                        }
                        PropInstParam propParam = new PropInstParam();
                        propParam.PropDefId = prop.Id;
                        propParam.Val = value;
                        propertyValues.Add(propParam);
                    }
                }

                // first add properties which don't exist on item yet
                if (addedPropDefIds.Count > 0)
                {
                    IEnumerable<long> masterIds = editableItems.Select(i => i.MasterId);

                    editableItems = ServiceManager.ItemService.UpdateItemPropertyDefinitions(masterIds.ToArray(), addedPropDefIds.ToArray(), null, "Property Edit");
                }
                // update property values
                if (propertyValues.Count > 0)
                {
                    IEnumerable<long> itemRevIds = editableItems.Select(i => i.RevId);
                    List<long> revIds = itemRevIds.ToList();
                    long revId = revIds[0];

                    editableItems = ServiceManager.ItemService.UpdateItemProperties(new long[] { revId }, new PropInstParamArray[] {
                            new PropInstParamArray() {
                                Items= propertyValues.ToArray()
                                }   
                        });

                    // update title
                    if (string.IsNullOrEmpty(title) == false)
                        {
                        foreach (Item editableItem in editableItems)
                            {
                            editableItem.Title = title;
                            }
                        }
                    // update description
                    if (string.IsNullOrEmpty(description) == false)
                        {
                        foreach (Item editableItem in editableItems)
                            {
                            editableItem.Detail = description;
                        }
                    }
                    //update units
                    if (string.IsNullOrEmpty(units) == false)
                    {
                        foreach (Item editableItem in editableItems)
                        {
                            editableItem.Units = units;
                        }
                    }
                    if (editableItems != null)
                    {
                        editableItems = editableItems.Distinct(new ItemIdComparer()).ToArray();
                    }
                    ServiceManager.ItemService.UpdateAndCommitItems(editableItems);
                }
                else
                {
                    // no property to update - undo changes
                    IEnumerable<long> ids = editableItems.Select(i => i.Id);

                    ServiceManager.ItemService.UndoEditItems(ids.ToArray());
                }
                editableItems = null;

                SeriLog.LogInformation($"Imported properties: {propertyValues.Count}");
                //Log(MessageCategory.Info, "Imported properties: {0}", propertyValues.Count);
            }
            catch (Exception)
            {
                if (editableItems != null)
                {
                    IEnumerable<long> ids = editableItems.Select(i => i.Id);
                    ServiceManager.ItemService.UndoEditItems(ids.ToArray());
                }
                try
                {
                    ServiceManager.ItemService.DeleteUncommittedItems(false);
                }
                catch (Exception e)
                {
                    SeriLog.LogDebug("Exception during cleanup " + VDF.Library.ExceptionParser.GetMessage(e));
                    //Log(MessageCategory.Debug, "Exception during cleanup " + VDF.Library.ExceptionParser.GetMessage(e));
                }
                throw;
            }
        }

        private Item GetItem(string number)
        {
            try
            {
                return ServiceManager.ItemService.GetLatestItemByItemNumber(number);
            }
            catch
            {
                //item not found
                return null;
            }
        }

        private CustEnt GetEntity(string definition, string name)
        {
            List<SrchCond> conditions = new List<SrchCond>();
            string[] propNames = new string[] { "CustomEntityName", "Name" };
            string[] propValues = new string[] { definition, name };

            for (int i = 0; i < propNames.Length; i++)
            {
                string propName = propNames[i],
                    propValue = propValues[i];
                PropDef propDef = PropertyDefinitions.FirstOrDefault(p => string.Equals(p.SysName, propName, StringComparison.InvariantCultureIgnoreCase));

                if (propDef == null)
                {

                    SeriLog.LogWarning($"Unable to locate property: {propName}");
                    //Log(MessageCategory.Warning, "Unable to locate property: {0}", propName);
                    return null;
                }
                SrchCond cond = new SrchCond
                {
                    PropDefId = propDef.Id,
                    PropTyp = PropertySearchType.SingleProperty,
                    SrchOper = 3,
                    SrchRule = SearchRuleType.Must,
                    SrchTxt = propValue,
                };

                conditions.Add(cond);
            }
            // do search
            List<CustEnt> results = new List<CustEnt>();
            SrchStatus status = null;
            string bookmark = string.Empty;

            while (status == null || results.Count < status.TotalHits)
            {
                CustEnt[] entities = ServiceManager.CustomEntityService.FindCustomEntitiesBySearchConditions(conditions.ToArray(), null, ref bookmark, out status);

                results.AddRange(entities);
            }
            return results.FirstOrDefault();
        }

        private void ProcessEntityRow(DataRow row)
        {
            string definition = Convert.ToString(row[kEntityDefinitionTag]),
                name = Convert.ToString(row[kEntityNameTag]);
            CustEnt entity = GetEntity(definition, name);

            if (entity == null)
            {
                SeriLog.LogWarning($"Unable to locate entity: {name}");
                //Log(MessageCategory.Warning, "Unable to locate entity: {0}", name);
            }
            entity = UpdateCategory(entity, row);
            List<PropInstParam> propertyValues = new List<PropInstParam>();

            foreach (DataColumn col in row.Table.Columns)
            {
                if (string.Equals(col.ColumnName, kEntityDefinitionTag, StringComparison.InvariantCultureIgnoreCase) ||
                    string.Equals(col.ColumnName, kEntityNameTag, StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }
                if (reservedColumns.Contains(col.ColumnName))
                {
                    continue;
                }
                PropDef propDef = PropertyDefinitions.FirstOrDefault(p => string.Equals(p.DispName, col.ColumnName, StringComparison.InvariantCultureIgnoreCase));

                if (propDef == null)
                {
                    SeriLog.LogWarning($"Unable to locate property: {col.ColumnName}");
                    //Log(MessageCategory.Warning, "Unable to locate property: {0}", col.ColumnName);
                    continue;
                }
                if (propDef.IsSys)
                {
                    SeriLog.LogWarning($"Can't update system property: {col.ColumnName}");
                    //Log(MessageCategory.Warning, "Can't update system property: {0}", col.ColumnName);
                    continue;
                }
                object value = GetPropertyValue(propDef, row.Field<string>(col));


                PropInstParam propParam = new PropInstParam();
                propParam.PropDefId = propDef.Id;
                propParam.Val = value;
                propertyValues.Add(propParam);
            }
            if (propertyValues.Any())
            {
                ServiceManager.CustomEntityService.UpdateCustomEntityProperties(new long[] { entity.Id },
                        new PropInstParamArray[] {
                            new PropInstParamArray() {
                                Items= propertyValues.ToArray()
                                }   
                        });

                SeriLog.LogInformation($"Imported properties: {propertyValues.Count}");
                //Log(MessageCategory.Info, "Imported properties: {0}", propertyValues.Count);
            }
            UpdateLifecycle(entity, row);
        }

        private CustEnt UpdateCategory(CustEnt entity, DataRow row)
        {
            if (row.Table.Columns[CATEGORY] == null)
            {
                return entity;
            }
            string categoryName = row.Field<string>(CATEGORY);

            if (string.IsNullOrWhiteSpace(categoryName))
            {
                SeriLog.LogDebug("No category found - will not update category");
                //Log(MessageCategory.Debug, "No category found - will not update category");
                return entity;
            }
            if (EntityCategories == null)
            {
                return entity;
            }
            Cat cat = EntityCategories.FirstOrDefault(c => string.Equals(c.Name, categoryName, StringComparison.InvariantCultureIgnoreCase));

            if (cat == null)
            {
                SeriLog.LogError($"Unable to locate category: {categoryName}");
                //Log(MessageCategory.Error, "Unable to locate category: {0}", categoryName);
                throw new Exception("Unable to locate category");
            }
            if (entity.Cat.CatId == cat.Id)
            {
                SeriLog.LogDebug($"Entity has already category ({categoryName}) set. No change");
                //Log(MessageCategory.Debug, "Entity has already category ({0}) set. No change", categoryName);
                return entity;
            }
            CustEnt[] updatedEntities =
                ServiceManager.CustomEntityService.UpdateCustomEntityCategories(new long[] { entity.Id }, new long[] { cat.Id });

            SeriLog.LogInformation($"Entity category changed to {cat.Name}");
            //Log(MessageCategory.Info, "Entity category changed to {0}", cat.Name);
            return updatedEntities.First();
        }

        private CustEnt UpdateLifecycle(CustEnt entity, DataRow row)
        {
            if (row.Table.Columns[LIFECYCLEDEF] == null)
            {
                return entity;
            }
            string definitionName = row.Field<string>(LIFECYCLEDEF);

            if (string.IsNullOrWhiteSpace(definitionName))
            {
                SeriLog.LogDebug("No lifecycle definition in input file - will not update lifecycle.");
                //Log(MessageCategory.Debug, "No lifecycle definition in input file - will not update lifecycle.");
                return entity;
            }
            string stateName = row.Field<string>(LIFECYCLESTATE);

            if (string.IsNullOrWhiteSpace(stateName))
            {
                SeriLog.LogDebug("No lifecycle state in input file - will not update lifecycle.");
                //Log(MessageCategory.Debug, "No lifecycle state in input file - will not update lifecycle.");
                return entity;
            }
            LfCycDef def = LfCycDefs.FirstOrDefault(d => string.Equals(d.DispName, definitionName, StringComparison.InvariantCultureIgnoreCase));

            if (def == null)
            {
                SeriLog.LogWarning($"Unable to locate lifecycle definition: {definitionName}");
                //Log(MessageCategory.Warning, "Unable to locate lifecycle definition: {0}", definitionName);
                throw new Exception("Unable to locate lifecycle definition");
            }
            LfCycState state = def.StateArray.FirstOrDefault(s => string.Equals(s.DispName, stateName, StringComparison.InvariantCultureIgnoreCase));

            if (state == null)
            {
                SeriLog.LogWarning($"Unable to locate lifecycle state: {stateName}");
                //Log(MessageCategory.Warning, "Unable to locate lifecycle state: {0}", stateName);
                throw new Exception("Unable to locate lifecycle state");
            }
            CustEnt[] updatedEntities = null;

            if (entity.LfCyc == null)
            {
                updatedEntities =
                    ServiceManager.CustomEntityService.UpdateCustomEntityLifeCycleDefinitions(new long[] { entity.Id }, new long[] { def.Id }, new long[] { state.Id }, "Update lifecycle");
                SeriLog.LogInformation("Updated lifecycle definition.");
                //Log(MessageCategory.Info, "Updated lifecycle definition.");
            }
            else
            {
                if (entity.LfCyc.LfCycDefId != def.Id)
                {
                    updatedEntities =
                        ServiceManager.CustomEntityService.UpdateCustomEntityLifeCycleDefinitions(new long[] { entity.Id }, new long[] { def.Id }, new long[] { state.Id }, "Update lifecycle");
                    SeriLog.LogInformation("Updated lifecycle definition.");
                    //Log(MessageCategory.Info, "Updated lifecycle definition.");
                }
                else if (entity.LfCyc.LfCycStateId != state.Id)
                {
                    updatedEntities =
                        ServiceManager.CustomEntityService.UpdateCustomEntityLifeCycleStates(new long[] { entity.Id }, new long[] { state.Id }, "Update lifecycle");
                    SeriLog.LogInformation("Updated lifecycle state.");
                    //Log(MessageCategory.Info, "Updated lifecycle state.");
                }
                else
                {
                    SeriLog.LogInformation("No lifecycle change required.");
                    //Log(MessageCategory.Info, "No lifecycle change required.");
                    return entity;
                }
            }
            return updatedEntities.First();
        }

        private PropDef GetProperty(string propertyName)
        {
            PropDef prop = PropertyDefinitions.SingleOrDefault(p => p.DispName == propertyName);

            if (prop == null)
            {
                prop = PropertyDefinitions.SingleOrDefault(p => p.SysName == propertyName);
            }
            return prop;
        }

        private object GetPropertyValue(PropDef definition, string rawValue)
        {
            try
            {
                if (string.IsNullOrEmpty(rawValue) == true)
                {
                    return null;
                }
                object propertyValue = null;

                if (definition.Typ == DataType.String)
                {
                    propertyValue = rawValue;
                }
                else if (definition.Typ == DataType.Numeric)
                {
                    propertyValue = Convert.ToDouble(rawValue);
                }
                else if (definition.Typ == DataType.DateTime)
                {
                    propertyValue = Convert.ToDateTime(rawValue);
                }
                else if (definition.Typ == DataType.Bool)
                {
                    propertyValue = Convert.ToBoolean(rawValue);
                }
                return propertyValue;
            }
            catch (Exception ex)
            {
                SeriLog.LogError(ex.Message,"Error while obtaining property value: ");
                //Log(MessageCategory.Error, "Error while obtaining property value: {0}", ex.Message);
                return null;
            }
        }

        private File GetFile(string fileName)
        {
            if (fileName.StartsWith("$") == true)
            {
                return GetFileByPath(fileName);
            }
            return GetFileByNameOnly(fileName);
        }

        private File GetFileByNameOnly(string name)
        {
            PropDef fileProp = GetProperty("ClientFileName");
            SrchCond cond = new SrchCond();

            cond.PropDefId = fileProp.Id;
            cond.PropTyp = PropertySearchType.SingleProperty;
            cond.SrchOper = 3;
            cond.SrchTxt = name;
            string bookmark = string.Empty;
            SrchStatus status;

            File[] files =
                ServiceManager.DocumentService.FindFilesBySearchConditions(new SrchCond[] { cond }, null, new long[] { RootFolder.Id }, true, true, ref bookmark, out status);

            if (files == null)
            {
                
                SeriLog.LogError(new Exception("File not found"), name);
                //SeriLog.LogInformation("");
                //throw new Exception("File not found.");
            }
            if (files.Length != 1)
            {
                SeriLog.LogError(new Exception("File name is not unique."), name);
                //SeriLog.LogInformation("");
                //throw new Exception("File name is not unique.");
            }
            return files[0];
        }

        private File GetFileByPath(string fileName)
        {
            int index = fileName.LastIndexOf('/');
            string name = fileName.Substring(index + 1);
            FilePathArray[] result =
                ServiceManager.DocumentService.GetLatestFilePathsByNames(new string[] { name });

            if (result == null)
            {
                SeriLog.LogError(new Exception("File not found"), fileName);
                //SeriLog.LogInformation("");
                //log.Debug(fileName, new Exception("File not found"));
                //throw new Exception("File not found.");
            }
            foreach (FilePathArray item in result)
            {
                if (item.FilePaths == null)
                {
                    continue;
                }
                foreach (FilePath filePath in item.FilePaths)
                {
                    if (0 == string.Compare(filePath.Path, fileName, true))
                    {
                        return filePath.File;
                    }
                }
            }
            SeriLog.LogError(new Exception("File not found"), fileName);
            //SeriLog.LogInformation("");
            //log.Debug(fileName, new Exception("File not found"));
            throw new Exception("File not found.");
        }

        private void Log(MessageCategory category, string message)
        {
            if (category == MessageCategory.Debug && Options.EnableDebugMessages == false)
            {
                return;
            }
            if (category == MessageCategory.Debug)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
            }
            if (category == MessageCategory.Warning)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
            }
            else if (category == MessageCategory.Error)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
            log.Info(message);
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private void Log(MessageCategory category, string format, params object[] args)
        {
            string message = string.Format(format, args);

            Log(category, message);
        }

    }
}
