using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;

namespace SdkGenerator
{
    public class ClassCreator
    {
        private CodeNamespace _codeNamespace;
        private CodeTypeDeclaration _targetClass;
        private CodeCompileUnit _targetUnit;

        public ClassCreator(string nameSpace)
        {
            _codeNamespace = new CodeNamespace(nameSpace);
        }

        public ClassCreator SetClassName(string className, bool useTypeT)
        {
            _targetUnit = new CodeCompileUnit();
            _targetClass = new CodeTypeDeclaration(className)
            {
                IsClass = true,
                TypeAttributes = TypeAttributes.Public
            };

            if (useTypeT)
            {
                _targetClass.TypeParameters.Add(new CodeTypeParameter("T"));
            }

            _codeNamespace.Types.Add(_targetClass);
            _targetUnit.Namespaces.Add(_codeNamespace);

            return this;
        }

        public ClassCreator AddImports(string importName)
        {
            _codeNamespace.Imports.Add(new CodeNamespaceImport(importName));

            return this;
        }

        public ClassCreator GenerateCSharpCode(string fileName)
        {
            var provider = CodeDomProvider.CreateProvider("CSharp");
            var options = new CodeGeneratorOptions();
            options.BracingStyle = "C";
            using (var sourceWriter = new StreamWriter(fileName))
            {
                provider.GenerateCodeFromCompileUnit(_targetUnit, sourceWriter, options);
            }

            return this;
        }


        public ClassCreator AddProperties(string propertyName, Type propertyType)
        {
            var backingField = new CodeMemberField(propertyType, "_" + propertyName);
            _targetClass.Members.Add(backingField);

            // Declare the read-only Width property.
            var memberProperty = new CodeMemberProperty
            {
                Attributes = MemberAttributes.Public | MemberAttributes.Final,
                Name = propertyName,
                HasGet = true,
                HasSet = true,
                Type = new CodeTypeReference(propertyType)
            };

            memberProperty.GetStatements.Add(new CodeMethodReturnStatement(
                new CodeFieldReferenceExpression(
                    new CodeThisReferenceExpression(), "_" + propertyName)));

            memberProperty.SetStatements.Add(
                new CodeAssignStatement(new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), "_" + propertyName),
                    new CodePropertySetValueReferenceExpression())
                );

            _targetClass.Members.Add(memberProperty);


            return this;
        }
    }
}