using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Microsoft.DotNet.CodeFormatting
{
    internal class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding { get { return Encoding.UTF8; } }
    }

    class ProjectConverter
    {
        private List<Workspace> TempWorkspaces = new List<Workspace>();
        private Dictionary<Workspace, string> TempFiles = new Dictionary<Workspace, string>();

        readonly string CSharpProjectTemplate = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""4.0"" DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <Import Project=""$(MSBuildExtensionsPath)/$(MSBuildToolsVersion)/Microsoft.Common.props"" Condition=""Exists('$(MSBuildExtensionsPath)/$(MSBuildToolsVersion)/Microsoft.Common.props')"" />
  <PropertyGroup>
    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>
    <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>
    <ProjectGuid>{GUID}</ProjectGuid>
    <OutputType>{OUTTYPE}</OutputType>
    <AppDesignerFolder>Properties</AppDesignerFolder>
    <RootNamespace>{NAMESPACE}</RootNamespace>
    <AssemblyName>{ASSEMBLY}</AssemblyName>
    <TargetFrameworkVersion>v3.5</TargetFrameworkVersion>
    <FileAlignment>512</FileAlignment>
  </PropertyGroup>
  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Debug|AnyCPU' "">
    <PlatformTarget>AnyCPU</PlatformTarget>
    <DebugSymbols>true</DebugSymbols>
    <DebugType>full</DebugType>
    <Optimize>false</Optimize>
    <OutputPath>bin/Debug/</OutputPath>
    <DefineConstants>DEBUG;TRACE</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>
  <PropertyGroup Condition="" '$(Configuration)|$(Platform)' == 'Release|AnyCPU' "">
    <PlatformTarget>AnyCPU</PlatformTarget>
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>bin/Release/</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
  </PropertyGroup>" +
  //<ItemGroup>
  //  <Reference Include=""System""/>
  //  <Reference Include=""System.Core""/>
  //  <Reference Include=""System.Xml.Linq""/>
  //  <Reference Include=""System.Data.DataSetExtensions""/>
  //  <Reference Include=""System.Data""/>
  //  <Reference Include=""System.Xml""/>
  //</ItemGroup>
  @"
  <ItemGroup>" +
  //  <Compile Include=""Class1.cs"" />
  //  <Compile Include=""Properties\AssemblyInfo.cs"" />
  @"
  </ItemGroup>
  <Import Project=""$(MSBuildToolsPath)/Microsoft.CSharp.targets"" />
</Project>
";
        private string LoadProjectTemplate(string name, string assemblyName, string language, OutputKind outputKind)
        {
            string template = "";
            if (language == LanguageNames.CSharp)
                template = this.CSharpProjectTemplate;

            // Set project GUID, Name and AssemblyName
            template = template.Replace("{GUID}", Guid.NewGuid().ToString("D"))
                               .Replace("{NAMESPACE}", name)
                               .Replace("{ASSEMBLY}", assemblyName);

            // Set output type
            string outputType = (outputKind == OutputKind.ConsoleApplication) ? "Console" :
                                (outputKind == OutputKind.WindowsApplication) ? "App" :
                                /*(outputKind == OutputKind.DynamicallyLinkedLibrary) ? "Library" :*/ "Library";
            template = template.Replace("{OUTTYPE}", outputType);

            return template;
        }

        internal static string CSharpDefaultFileExt = "cs";
        internal static string VisualBasicDefaultExt = "vb";
        IList<string> GetSourceDocuments(Project project)
        {
            // Add all source files in project directory into new project
            var sourceFiles = new List<string>();

            // search all files with file extension
            string rootPath = Path.GetDirectoryName(project.FilePath);
            string filter = "*." + ((project.Language == LanguageNames.CSharp) ? CSharpDefaultFileExt : VisualBasicDefaultExt);

            // Ignore files in intermediate directory
            List<string> ignorePaths = new List<string>();
            ignorePaths.Add(Path.Combine(rootPath, "obj"));

            foreach (var sourceFile in Directory.EnumerateFiles(rootPath, filter, SearchOption.AllDirectories))
            {
                // Check file is in ignore paths
                bool bIgnore = false;
                foreach (var ignorePath in ignorePaths)
                {
                    if (sourceFile.StartsWith(ignorePath))
                    {
                        bIgnore = true;
                        break;
                    }
                }
                if (!bIgnore)
                {
                    // Convert address to relative path and save it
                    sourceFiles.Add(sourceFile.Substring(rootPath.Length + 1).Replace('\\', '/'));
                }
            }

            return sourceFiles;
        }

        /// <summary>
        /// Create project template string with given parameters
        /// </summary>
        /// <param name="name">Project name</param>
        /// <param name="assemblyName">Project assembly name</param>
        /// <param name="language">Project language (C# or VB)</param>
        /// <param name="outputKind">Output type (Console, Exe or Library)</param>
        /// <param name="sourceFiles">Project source files</param>
        /// <returns></returns>
        string CreateProjectWith(string name, string assemblyName, string language, OutputKind outputKind, IList<string> sourceFiles)
        {
            string template = this.LoadProjectTemplate(name, assemblyName, language, outputKind);

            // Load project as XML
            var document = new XmlDocument();
            document.LoadXml(template);

            // Add files into document
            var itemGroup = document.GetElementsByTagName("ItemGroup")[0];
            foreach (var sourceFile in sourceFiles)
            {
                var item = document.CreateElement("Compile", document.DocumentElement.NamespaceURI);
                item.SetAttribute("Include", sourceFile);
                itemGroup.AppendChild(item);
            }

            // Return project template string
            StringWriter writer = new Utf8StringWriter();
            document.Save(writer);
            return writer.ToString();
        }

        public bool NeedsUpdate(Project project)
        {
            return project.DocumentIds.Count() == 0;
        }

        /// <summary>
        /// This function is for mono environment only.
        /// Old version of MSBuild Mono, doesn't load project files which contains '\' as a path separator.
        /// In that case, we can replace '\' to '/', which is compatible in Windows and Unix both.
        /// </summary>
        /// <param name="solution"></param>
        /// <returns></returns>
        public bool NeedsUpdate(Solution solution)
        {
            if (solution.FilePath == null)
                return false;

            // Solution file is not a XML file and it contains names, keyworks and GUID only.
            // If the chatacter '\' is existing, it's used as a path separator.
            // (Not sure it's correct.  Please update this function if any exception occurs)
            string solutionBody = File.ReadAllText(solution.FilePath);
            return solutionBody.Contains('\\');
        }

        public async Task<Workspace> UpdateProjectAsync(Project project, CancellationToken cancellationToken)
        {
            try
            {
                if (project.Documents.Count() > 0)
                {
                    // This project has document files.
                    // It means the project version is lower and there's no need to downgrade
                    return project.Solution.Workspace;
                }

                // Add source files
                IList<string> sourceFiles = this.GetSourceDocuments(project);
                if (sourceFiles.Count() == 0)
                    return project.Solution.Workspace;

                // Create project file content and save as temporary file
                string tmpProjectPath = project.FilePath.Insert(project.FilePath.LastIndexOf('.'), "_tmp");
                string projectString = this.CreateProjectWith(project.Name, project.AssemblyName, project.Language,
                                                            project.CompilationOptions.OutputKind, sourceFiles);
                File.WriteAllText(tmpProjectPath, projectString);

                // Load project from temporary file
                var workspace = MSBuildWorkspace.Create();
                {
                    workspace.LoadMetadataForReferencedProjects = true;
                    var newProject = await workspace.OpenProjectAsync(tmpProjectPath, cancellationToken);

                    this.TempFiles.Add(workspace, tmpProjectPath);
                }
                this.TempWorkspaces.Add(workspace);

                return workspace;
            }
            catch (Exception /*ex*/)
            {
                // Debug.WriteLine(ex.Message);
                return project.Solution.Workspace;
            }
        }

        public async Task<Workspace> UpdateSolutionAsync(Solution solution, CancellationToken cancellationToken)
        {
            try
            {
                if (NeedsUpdate(solution) == false)
                {
                    return solution.Workspace;
                }

                // Create solution file content and save as temporary file
                string tmpSolutionPath = solution.FilePath.Insert(solution.FilePath.LastIndexOf('.'), "_tmp");
                string solutionString = File.ReadAllText(solution.FilePath);
                solutionString.Replace('\\', '/');

                File.WriteAllText(tmpSolutionPath, solutionString);

                // Load solution from temporary file
                var workspace = MSBuildWorkspace.Create();
                {
                    workspace.LoadMetadataForReferencedProjects = true;
                    var newSolution = await workspace.OpenSolutionAsync(tmpSolutionPath, cancellationToken);

                    this.TempFiles.Add(workspace, tmpSolutionPath);
                }
                this.TempWorkspaces.Add(workspace);

                return workspace;
            }
            catch (Exception /*ex*/)
            {
                // Debug.WriteLine(ex.Message);
                return solution.Workspace;
            }
        }

        public void ClearWorkspace(Workspace workspace)
        {
            // Clear temporary created workspaces
            if (this.TempWorkspaces.Remove(workspace))
            {
                // Delete all temp files associated with project
                if (this.TempFiles.ContainsKey(workspace))
                {
                    File.Delete(this.TempFiles[workspace]);
                    this.TempFiles.Remove(workspace);
                }

                // Dispose workspace
                workspace.Dispose();
            }
        }
    }
}
