﻿using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using NUnit.Framework;
using Toscana.Engine;

namespace Toscana.Tests.Engine
{
    [TestFixture]
    public class ToscaServiceTemplateLoaderTests
    {
        [Test]
        public void Loading_Leaf_Tosca_File_With_Imports_Should_Load_Dependent_Files()
        {
            var fileSystem = new MockFileSystem();

            fileSystem.AddFile("some_defs.yaml", new MockFileData(@"
tosca_definitions_version: tosca_simple_yaml_1_0
description: Common definitions
node_types:
    common_node_type:
        properties:
          num_cpus:
            type: integer
"));

            fileSystem.AddFile("user_tosca.yaml", new MockFileData(@"
tosca_definitions_version: tosca_simple_yaml_1_0
description: User tosca description
imports:
  - some_definition_file: some_defs.yaml"));

            var toscaSimpleProfile = new Bootstrapper()
                .Replace<IFileSystem>(fileSystem)
                .GetToscaServiceTemplateLoader()
                .Load("user_tosca.yaml");

            // Assert
            toscaSimpleProfile.Description.Should().Be("User tosca description");
            toscaSimpleProfile.NodeTypes["common_node_type"].Properties["num_cpus"].Type.Should().Be("integer");
        }

        [Test]
        public void Loading_Import_From_Alternative_Path()
        {
            var fileSystem = new MockFileSystem();

            fileSystem.AddFile(@"C:\alternative\tosca\some_defs.yaml", new MockFileData(@"
tosca_definitions_version: tosca_simple_yaml_1_0
node_types:
    common_node_type:
        properties:
          num_cpus:
            type: integer
"));

            fileSystem.AddFile("user_tosca.yaml", new MockFileData(@"
tosca_definitions_version: tosca_simple_yaml_1_0
description: User tosca description
imports:
  - some_definition_file: some_defs.yaml"));

            var toscaSimpleProfile = new Bootstrapper()
                .Replace<IFileSystem>(fileSystem)
                .GetToscaServiceTemplateLoader()
                .Load("user_tosca.yaml", @"C:\alternative\tosca\");

            // Assert
            toscaSimpleProfile.Description.Should().Be("User tosca description");
            toscaSimpleProfile.NodeTypes["common_node_type"].Properties["num_cpus"].Type.Should().Be("integer");
        }
    }
}