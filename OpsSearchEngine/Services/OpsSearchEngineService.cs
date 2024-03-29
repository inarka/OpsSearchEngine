﻿using AutoMapper;
using OpsSearchEngine.Interfaces;
using OpsSearchEngine.Models;
using OpsSearchEngine.Models.XML;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpsSearchEngine.Services
{
	public class OpsSearchEngineService : IEngine
	{
		private readonly IXmlStringReader _xmlReader;
		private readonly IXmlDeserializer<Project> _xmlDeserializer;
		private readonly ITrie _trie;
		private readonly IMapper _mapper;

		private ImmutableDictionary<string, Modul> _modules;

		public OpsSearchEngineService(IXmlStringReader xmlReader, IXmlDeserializer<Project> xmlDeserializer, ITrie trie, IMapper mapper)
		{
			_xmlReader = xmlReader;
			_xmlDeserializer = xmlDeserializer;
			_trie = trie;
			_mapper = mapper;
		}

		public ModulesResponse FindModules(PatientInfo patientInfo)
		{
			var input = _xmlReader.ReadXml();

			var project = _xmlDeserializer.Deserialize(input);

			_modules = GetModules(project).ToImmutableDictionary();

			var opsCodesTrieRoot = _trie.Build(project);

			var response = new ModulesResponse();

			foreach (var ops in patientInfo.OpsCodes)
			{
				var candidate = _trie.FindNode(opsCodesTrieRoot, ops);

				if (candidate == null)
				{
					continue;
				}

				ChooseCorrectAgeModule(candidate.ModuleInclExcls, patientInfo.Age);

				ChooseCorrectInclExclModule(candidate.ModuleInclExcls, patientInfo.OpsCodes);

				if (!candidate.ModuleInclExcls.Any())
				{
					continue;
				}

				response.Modules.Add(ops, new List<ModuleResponse>());

				foreach (var match in candidate.ModuleInclExcls)
				{
					var module = _modules[match.ModulName];

					var moduleResponse = _mapper.Map<Modul, ModuleResponse>(module);

					if (!string.IsNullOrEmpty(module.EndoOps))
					{
						moduleResponse.IsEndo = IsEndoOps(ops, module.EndoOps);
					}

					response.Modules[ops].Add(moduleResponse);
				}
			}

			return response;
		}

		private Dictionary<string, Modul> GetModules(Project project)
		{
			var result = new Dictionary<string, Modul>();

			foreach (var modul in project.Module.Moduls)
			{
				result.Add(modul.Name, modul);
			}

			return result;
		}

		private void ChooseCorrectInclExclModule(List<ModuleInclExcl> modules, List<string> opsCodes)
		{		
			foreach (var module in modules.ToList())
			{
				bool excludeMatches = false;

				bool includeMatches = false;

				foreach (var ops in opsCodes)
				{
					if (module.Excludes != null && PatternMatches(ops, module.Excludes))
					{
						excludeMatches = true;
						break;
					}

					if (!includeMatches && (module.Includes == null || PatternMatches(ops, module.Includes)))
					{
						includeMatches = true;
					}
				}

				if (excludeMatches || !includeMatches)
				{
					modules.Remove(module);
				}
			}
		}

		private void ChooseCorrectAgeModule(List<ModuleInclExcl> modules, int age)
		{
			if (modules.Count == 1)
			{
				return;
			}

			if (!modules.Any(x => _modules[x.ModulName].StartAlter != 0 && _modules[x.ModulName].BisAlter != 0))
			{
				return;
			}

			modules = modules.Where(x => _modules[x.ModulName].StartAlter <= age && _modules[x.ModulName].BisAlter >= age).ToList();
		}
		private bool IsEndoOps(string opsCode, string moduleEndoOpses)
		{
			var endoOpsList = moduleEndoOpses.Split(';');

			return PatternMatches(opsCode, endoOpsList.ToList());
		}
		private bool PatternMatches(string opsCode, List<string> inputPatternOpses)
		{
			foreach (var inputOps in inputPatternOpses)
			{
				if (inputOps.Contains('_'))
				{
					var regex = new Regex(inputOps.Replace(".", "[.]").Replace("_", @"\S+"));

					if (regex.IsMatch(opsCode))
					{
						return true;
					}
				}

				else
				{
					if (opsCode.StartsWith(inputOps))
					{
						return true;
					}
				}
			}

			return false;
		}
	}
}
