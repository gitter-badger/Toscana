using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Linq;
using Toscana.Common;
using Toscana.Exceptions;

namespace Toscana.Engine
{
    public interface IToscaCloudServiceArchiveLoader
    {
        /// <summary>
        /// Loads Cloud Service Archive (CSAR) file and all its dependencies
        /// </summary>
        /// <param name="archiveFilePath">Path to Cloud Service Archive (CSAR) zip file</param>
        /// <param name="alternativePath">Path for dependencies lookup outside the archive</param>
        /// <exception cref="Toscana.Exceptions.ToscaCloudServiceArchiveFileNotFoundException">Thrown when CSAR file not found.</exception>
        /// <exception cref="Toscana.Exceptions.ToscaMetadataFileNotFound">Thrown when TOSCA.meta file not found in the archive.</exception>
        /// <exception cref="Toscana.Exceptions.ToscaImportFileNotFoundException">Thrown when import file neither found in the archive nor at the alternative path.</exception>
        /// <returns>A valid instance of ToscaCloudServiceArchive</returns>
        ToscaCloudServiceArchive Load(string archiveFilePath, string alternativePath = null);

        /// <summary>
        /// Loads Cloud Service Archive (CSAR) file and all its dependencies
        /// </summary>
        /// <param name="archiveStream">Stream to Cloud Service Archive (CSAR) zip file</param>
        /// <param name="alternativePath">Path for dependencies lookup outside the archive</param>
        /// <exception cref="Toscana.Exceptions.ToscaMetadataFileNotFound">Thrown when TOSCA.meta file not found in the archive.</exception>
        /// <exception cref="Toscana.Exceptions.ToscaImportFileNotFoundException">Thrown when import file neither found in the archive nor at the alternative path.</exception>
        /// <returns>A valid instance of ToscaCloudServiceArchive</returns>
        ToscaCloudServiceArchive Load(Stream archiveStream, string alternativePath = null);
    }

    public class ToscaCloudServiceArchiveLoader : IToscaCloudServiceArchiveLoader
    {
        private readonly IFileSystem fileSystem;
        private readonly IToscaParser<ToscaMetadata> metadataParser;
        private readonly IToscaParser<ToscaServiceTemplate> serviceTemplateParser;

        public ToscaCloudServiceArchiveLoader(IFileSystem fileSystem,
            IToscaParser<ToscaMetadata> metadataParser, IToscaParser<ToscaServiceTemplate> serviceTemplateParser)
        {
            this.fileSystem = fileSystem;
            this.metadataParser = metadataParser;
            this.serviceTemplateParser = serviceTemplateParser;
        }

        /// <summary>
        /// Loads Cloud Service Archive (CSAR) file and all its dependencies
        /// </summary>
        /// <param name="archiveFilePath">Path to Cloud Service Archive (CSAR) zip file</param>
        /// <param name="alternativePath">Path for dependencies lookup outside the archive</param>
        /// <exception cref="Toscana.Exceptions.ToscaCloudServiceArchiveFileNotFoundException">Thrown when CSAR file not found.</exception>
        /// <exception cref="Toscana.Exceptions.ToscaMetadataFileNotFound">Thrown when TOSCA.meta file not found in the archive.</exception>
        /// <exception cref="Toscana.Exceptions.ToscaImportFileNotFoundException">Thrown when import file neither found in the archive nor at the alternative path.</exception>
        /// <returns>A valid instance of ToscaCloudServiceArchive</returns>
        public ToscaCloudServiceArchive Load(string archiveFilePath, string alternativePath = null)
        {
            if (!fileSystem.File.Exists(archiveFilePath))
            {
                throw new ToscaCloudServiceArchiveFileNotFoundException(string.Format("Cloud Service Archive (CSAR) file '{0}' not found", archiveFilePath));
            }
            using (var zipToOpen = fileSystem.File.OpenRead(archiveFilePath))
            {
                return Load(zipToOpen, alternativePath);
            }
        }

        /// <summary>
        /// Loads Cloud Service Archive (CSAR) file and all its dependencies
        /// </summary>
        /// <param name="archiveStream">Stream to Cloud Service Archive (CSAR) zip file</param>
        /// <param name="alternativePath">Path for dependencies lookup outside the archive</param>
        /// <exception cref="Toscana.Exceptions.ToscaMetadataFileNotFound">Thrown when TOSCA.meta file not found in the archive.</exception>
        /// <exception cref="Toscana.Exceptions.ToscaImportFileNotFoundException">Thrown when import file neither found in the archive nor at the alternative path.</exception>
        /// <returns>A valid instance of ToscaCloudServiceArchive</returns>
        public ToscaCloudServiceArchive Load(Stream archiveStream, string alternativePath = null)
        {
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read))
            {
                var archiveEntries = archive.GetArchiveEntriesDictionary();
                var toscaMetaArchiveEntry = GetToscaMetaArchiveEntry(archiveEntries);
                var relativePath = Path.GetDirectoryName(toscaMetaArchiveEntry.FullName) ?? string.Empty;
                var toscaMetadata = metadataParser.Parse(toscaMetaArchiveEntry.Open());
                var toscaCloudServiceArchive = new ToscaCloudServiceArchive(toscaMetadata, archiveEntries);
                LoadDependenciesRecursively(toscaCloudServiceArchive, archiveEntries, toscaMetadata.EntryDefinitions, alternativePath, relativePath);
                toscaCloudServiceArchive.FillDefaults();
                return toscaCloudServiceArchive;
            }
        }

        private void LoadDependenciesRecursively(ToscaCloudServiceArchive toscaCloudServiceArchive, IReadOnlyDictionary<string, ZipArchiveEntry> zipArchiveEntries, string toscaServiceTemplateName, string alternativePath, string relativePath)
        {
            var toscaServiceTemplate = LoadToscaServiceTemplate(alternativePath, relativePath, zipArchiveEntries, toscaServiceTemplateName, toscaCloudServiceArchive);
            toscaCloudServiceArchive.AddToscaServiceTemplate(toscaServiceTemplateName, toscaServiceTemplate);
            foreach (var importFile in toscaServiceTemplate.Imports.SelectMany(import => import.Values))
            {
                LoadDependenciesRecursively(toscaCloudServiceArchive, zipArchiveEntries, importFile.File, alternativePath, relativePath);
            }
        }

        private ToscaServiceTemplate LoadToscaServiceTemplate(string alternativePath, string relativePath, IReadOnlyDictionary<string, ZipArchiveEntry> archiveEntries, string filePath, ToscaCloudServiceArchive toscaCloudServiceArchive)
        {
            var zipEntryFileName = Path.Combine(relativePath, filePath);
            var importStream = GetImportStream(alternativePath, archiveEntries, zipEntryFileName);
            try
            {
                return serviceTemplateParser.Parse(importStream);
            }
            catch (ToscaBaseException toscaBaseException)
            {
                throw new ToscaParsingException(
                    string.Format("Failed to load definitions file {0} due to an error: {1}", zipEntryFileName, toscaBaseException.GetaAllMessages()));
            }
        }

        private Stream GetImportStream(string alternativePath, IReadOnlyDictionary<string, ZipArchiveEntry> archiveEntries, string zipEntryFileName)
        {
            ZipArchiveEntry zipArchiveEntry;
            if (!archiveEntries.TryGetValue(zipEntryFileName, out zipArchiveEntry))
            {
                if (!string.IsNullOrEmpty(alternativePath))
                {
                    var alternativeFullPath = fileSystem.Path.Combine(alternativePath, zipEntryFileName);
                    if (!fileSystem.File.Exists(alternativeFullPath))
                    {
                        throw new ToscaImportFileNotFoundException(
                            string.Format(
                                "{0} file neither found within TOSCA Cloud Service Archive nor at alternative location '{1}'.",
                                zipEntryFileName, alternativePath));
                    }
                    return fileSystem.File.OpenRead(alternativeFullPath);
                }
                throw new ToscaImportFileNotFoundException(
                    string.Format("{0} file not found within TOSCA Cloud Service Archive file.", zipEntryFileName));
            }
            return zipArchiveEntry.Open();
        }

        private static ZipArchiveEntry GetToscaMetaArchiveEntry(IReadOnlyDictionary<string, ZipArchiveEntry> fillZipArchivesDictionary)
        {
            var toscaMetaArchiveEntry = fillZipArchivesDictionary.Values.FirstOrDefault(
                a =>
                    string.Compare(a.Name, ToscaMetaFileName,
                        StringComparison.InvariantCultureIgnoreCase) == 0);
            if (toscaMetaArchiveEntry == null)
            {
                throw new ToscaMetadataFileNotFound(
                    string.Format("{0} file not found within TOSCA Cloud Service Archive file.", ToscaMetaFileName)); 
            }
            return toscaMetaArchiveEntry;
        }

        private const string ToscaMetaFileName = "TOSCA.meta";
    }
}