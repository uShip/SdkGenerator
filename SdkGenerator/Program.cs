using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Routing;
using Microsoft.CSharp;
using uShip.Api.Http.Security;

namespace SdkGenerator
{
    public class Program
    {

        [STAThread]
        private static int Main(string[] args) // todo: add unit tests and integration tests - for multiple httpbody parameters etc
        {
            args = new[] { @"C:\inetpub\wwwroot\uShipTrunk3\uShip.API\bin\uShip.Api.dll", "uship.webapi.sdk", @"c:\temp\data\" };

            if (args.Length < 3)
            {
                Console.WriteLine("Usage: Generator.exe <path to an assembly with web api controllers> <generated namespace> <output folder>");
                return -1;
            }
            var sourceAssembly = args[0];
            var generatedNamespace = args[1];
            var outputFolder = args[2];
            try
            {
                GenerateWebApiClients(sourceAssembly, generatedNamespace, outputFolder);
                Console.WriteLine("Controller clients were regenerated");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Debug.WriteLine(ex);
                return -2;
            }
            return 0;
        }

        private static bool ShouldRegenerateControllerClients(string sourceAssembly, string outputFolder)
        {
            var assemblyWriteTime = File.GetLastWriteTime(sourceAssembly);
            var files = Directory.GetFiles(outputFolder, "*ControllerClient.cs");
            var shouldRegenerate = false;
            foreach (var file in files)
            {
                var sourceFileWriteTime = File.GetLastWriteTime(file);
                if (assemblyWriteTime > sourceFileWriteTime)
                {
                    shouldRegenerate = true;
                }
            }
            return !files.Any() || shouldRegenerate;
        }

        // http://msdn.microsoft.com/en-us/library/system.codedom.compiler.codedomprovider%28VS.80%29.aspx
        // http://stackoverflow.com/questions/484489/is-there-a-way-to-programmatically-extract-an-interface
        private static void GenerateWebApiClients(string sourceAssembly, string generatedNamespace, string outputFolder)
        {
            var assembly = Assembly.LoadFrom(sourceAssembly);
            var apiControllerTypes = assembly
                .GetTypes()
                .Where(x =>
                    (typeof(ApiControllerBase).IsAssignableFrom(x) ||
                    typeof(ApiController).IsAssignableFrom(x)) && !x.IsAbstract);



            foreach (var controllerType in apiControllerTypes)
            {

                var methodInfos = controllerType.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
                var controllerName = controllerType.Name.Replace("Controller", "");

                var interfaceNamespace = new CodeNamespace(generatedNamespace);
                var interfaceCompileUnit = new CodeCompileUnit();
                interfaceCompileUnit.Namespaces.Add(interfaceNamespace);
                var interfaceName = string.Format("I{0}ControllerClient", controllerName);
                var interface1 = new CodeTypeDeclaration(interfaceName) { IsInterface = true };
                interfaceNamespace.Types.Add(interface1);

                var classNamespace = new CodeNamespace(generatedNamespace);
                var classCompileUnit = new CodeCompileUnit();
                classCompileUnit.Namespaces.Add(classNamespace);
                var class1 = new CodeTypeDeclaration(string.Format("{0}ControllerClient", controllerName));
                //                class1.BaseTypes.Add(new CodeTypeReference(typeof(BaseApiControllerClient)));
                class1.BaseTypes.Add(new CodeTypeReference(interfaceName));
                classNamespace.Types.Add(class1);

                var constructor = new CodeConstructor
                {
                    Attributes = MemberAttributes.Public
                };
                constructor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(string), "serverUrl"));
                constructor.Parameters.Add(new CodeParameterDeclarationExpression(typeof(RouteCollection), "routes"));
                constructor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("serverUrl"));
                constructor.BaseConstructorArgs.Add(new CodeArgumentReferenceExpression("routes"));
                class1.Members.Add(constructor);

                foreach (var methodInfo in methodInfos)
                {
                    var method = new CodeMemberMethod { Name = methodInfo.Name, Attributes = MemberAttributes.Public };
                    var asyncMethod = new CodeMemberMethod { Name = methodInfo.Name + "Async", Attributes = MemberAttributes.Public };

                    var routeValuesDeclarationStatement = new CodeVariableDeclarationStatement(
                        typeof(RouteValueDictionary), "routeValues", new CodeObjectCreateExpression(typeof(RouteValueDictionary)));
                    var routeValuesReference = new CodeVariableReferenceExpression("routeValues");

                    method.Statements.Add(routeValuesDeclarationStatement);
                    asyncMethod.Statements.Add(routeValuesDeclarationStatement);

                    var parameterInfos = methodInfo.GetParameters();

                    var isPost = methodInfo.GetCustomAttributes(typeof(HttpPostAttribute), true).Any();
                    var numberOfHttpBodyParameters = 0;

                    var noParameterHasFromBodyAttribute = !parameterInfos.Any(x => x.GetCustomAttributes(typeof(FromBodyAttribute), true).Any());
                    var allParametersAreSimpleType = parameterInfos.All(x => IsSimpleType(x.ParameterType));
                    //                    if (isPost && noParameterHasFromBodyAttribute && allParametersAreSimpleType)
                    //                    {
                    //                        throw new Exception(string.Format("{0}.{1} parameters are all of simple type and no one has FromBody attribute", controllerType.Name, methodInfo.Name));
                    //                    }

                    CodeVariableReferenceExpression httpBodyParameterVariableReference = null;
                    foreach (var parameterInfo in parameterInfos)
                    {
                        var hasFromBodyAttribute = parameterInfo.GetCustomAttributes(typeof(FromBodyAttribute), true).Any();
                        var isComplexType = !IsSimpleType(parameterInfo.ParameterType);
                        var isHttpBodyParameter = hasFromBodyAttribute || isComplexType;
                        if (isHttpBodyParameter)
                        {
                            isPost = true;
                            numberOfHttpBodyParameters++;
                        }

                        //                        if (isPost && numberOfHttpBodyParameters > 1) throw new Exception(string.Format("{0}.{1} can have only one http body parameter", controllerType.Name, methodInfo.Name));


                        if (!parameterInfo.ParameterType.FullName.StartsWith("System"))
                        {
                           WriteClassProperties(parameterInfo.ParameterType, outputFolder);                            

                        }

                        method.Parameters.Add(new CodeParameterDeclarationExpression(parameterInfo.ParameterType, parameterInfo.Name));
                        asyncMethod.Parameters.Add(new CodeParameterDeclarationExpression(parameterInfo.ParameterType, parameterInfo.Name));

                        if (!isHttpBodyParameter)
                        {
                            var addParameterToRouteValues = new CodeMethodInvokeExpression(routeValuesReference, "Add",
                                new CodePrimitiveExpression(parameterInfo.Name), new CodeVariableReferenceExpression(parameterInfo.Name));
                            method.Statements.Add(addParameterToRouteValues);
                            asyncMethod.Statements.Add(addParameterToRouteValues);
                        }
                        else
                        {
                            httpBodyParameterVariableReference = new CodeVariableReferenceExpression(parameterInfo.Name);
                        }
                    }

                    method.ReturnType = new CodeTypeReference(methodInfo.ReturnType);
                    var returnTypeIsVoid = methodInfo.ReturnType == typeof(void);
                    if (returnTypeIsVoid)
                    {
                        asyncMethod.ReturnType = new CodeTypeReference(typeof(Task));
                    }
                    else
                    {
                        var taskType = typeof(Task<>);

                        if (!methodInfo.ReturnType.FullName.StartsWith("System"))
                        {
                             WriteClassProperties(methodInfo.ReturnType, outputFolder);
                            
                        }

                        var returnAsyncType = taskType.MakeGenericType(methodInfo.ReturnType);
                        asyncMethod.ReturnType = new CodeTypeReference(returnAsyncType);
                    }

                    interface1.Members.Add(method);
                    interface1.Members.Add(asyncMethod);

                    var typeParameters = returnTypeIsVoid
                        ? new CodeTypeReference[0]
                        : new[] { method.ReturnType };

                    var codeMethodReferenceExpression = new CodeMethodReferenceExpression(
                        new CodeThisReferenceExpression(),
                        isPost ? "HttpClientPost" : "HttpClientGet",
                        typeParameters);
                    var statement = isPost
                        ? new CodeMethodInvokeExpression(codeMethodReferenceExpression, new CodePrimitiveExpression(methodInfo.Name), httpBodyParameterVariableReference, routeValuesReference)
                        : new CodeMethodInvokeExpression(codeMethodReferenceExpression, new CodePrimitiveExpression(methodInfo.Name), routeValuesReference);

                    var asyncCodeMethodReferenceExpression = new CodeMethodReferenceExpression(
                        new CodeThisReferenceExpression(),
                        isPost ? "HttpClientPostAsync" : "HttpClientGetAsync",
                        typeParameters);
                    var asyncStatement = isPost
                        ? new CodeMethodInvokeExpression(asyncCodeMethodReferenceExpression, new CodePrimitiveExpression(methodInfo.Name), httpBodyParameterVariableReference, routeValuesReference)
                        : new CodeMethodInvokeExpression(asyncCodeMethodReferenceExpression, new CodePrimitiveExpression(methodInfo.Name), routeValuesReference);
                    var asyncCodeMethodReturnStatement = new CodeMethodReturnStatement(asyncStatement);

                    if (returnTypeIsVoid)
                    {
                        method.Statements.Add(statement);
                        asyncMethod.Statements.Add(asyncCodeMethodReturnStatement);
                    }
                    else
                    {
                        method.Statements.Add(new CodeMethodReturnStatement(statement));
                        asyncMethod.Statements.Add(asyncCodeMethodReturnStatement);
                    }

                    class1.Members.Add(method);
                    class1.Members.Add(asyncMethod);
                }

                CodeDomProvider provider = new CSharpCodeProvider();

                var interfaceFileName = string.Format(@"{0}\I{1}ControllerClient.cs", outputFolder, controllerName);
                GenerateCode(interfaceFileName, provider, interfaceCompileUnit);

                var classFileName = string.Format(@"{0}\{1}ControllerClient.cs", outputFolder, controllerName);
                GenerateCode(classFileName, provider, classCompileUnit);
            }
        }

        private static void GenerateCode(string sourceFileName, CodeDomProvider provider, CodeCompileUnit compileunit)
        {
            var tw = new IndentedTextWriter(new StreamWriter(sourceFileName, false), "    ");
            provider.GenerateCodeFromCompileUnit(compileunit, tw, new CodeGeneratorOptions());
            tw.Close();
        }

        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive || type == typeof(string);
        }


        public static void WriteClassProperties(Type classType, string outputFolder)
        {
            var className = classType.Name.Replace("`1", "");

            if (!File.Exists(outputFolder + className + ".cs"))
            {
                var creator = new ClassCreator(classType.Namespace);

                if (classType.GetGenericArguments().Length > 0)
                {
                    foreach (var genericArgument in classType.GetGenericArguments())
                    {
                        if (!genericArgument.IsEnum)
                        {
                            WriteClassProperties(genericArgument, outputFolder);
                        }

                        if (genericArgument.IsEnum)
                        {
                            WriteEnum(genericArgument, outputFolder);
                        }
                    }
                }

                var useTypeT = classType.IsConstructedGenericType;

                creator.SetClassName(className, useTypeT);

                foreach (var prop in classType.GetProperties())
                {
                    if (prop.PropertyType.IsGenericType)
                    {
                        foreach (var t in prop.PropertyType.GetGenericArguments())
                        {
                            if (t.IsClass && !t.FullName.StartsWith("System"))
                            {
                                WriteClassProperties(t, outputFolder);
                            }

                            if (t.IsEnum && !t.FullName.StartsWith("System"))
                            {
                                WriteEnum(t, outputFolder);
                            }
                        }

                        creator.AddProperties(prop.Name, prop.PropertyType);
                    }
                    else
                    {
                        if (prop.PropertyType.IsClass && !prop.PropertyType.FullName.StartsWith("System"))
                        {
                            WriteClassProperties(prop.PropertyType, outputFolder);
                        }

                        creator.AddProperties(prop.Name, prop.PropertyType);
                    }
                }

                creator.GenerateCSharpCode(outputFolder + className + ".cs");
            }
        }

        private static void WriteEnum(Type genericArgument, string outputFolder)
        {
            var enumName = genericArgument.Name.Replace("`1", "");
            var enumDestination = outputFolder + enumName + ".cs";
            if (!File.Exists(enumDestination))
            {
                new EnumCreator(genericArgument.Namespace).SetClassName(genericArgument.Name, false).AddEnumMembersFromType(genericArgument).GenerateCSharpCode(enumDestination);
            }
        }
    }


    public class EnumCreator
    {
        private CodeNamespace _codeNamespace;
        private CodeTypeDeclaration _targetClass;
        private CodeCompileUnit _targetUnit;

        public EnumCreator(string nameSpace)
        {
            _codeNamespace = new CodeNamespace(nameSpace);
        }

        public EnumCreator SetClassName(string className, bool useTypeT)
        {
            var _className = className;
            _targetUnit = new CodeCompileUnit();
            _targetClass = new CodeTypeDeclaration(className)
            {
                IsEnum = true,
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

        public EnumCreator AddEnumMembersFromType(Type enumType)
        {
            var values = Enum.GetValues(enumType);

            for (var i = 0; i < values.Length; i++)
            {
                int value;
                try
                {

                    value = (int)values.GetValue(i);
                }
                catch (Exception e)
                {
                    value = 0;
                }
                var name = Enum.GetName(enumType, value);
                AddMember(name, value);
            }


            return this;
        }

        private void AddMember(string memberName, object value)
        {
            var member = new CodeMemberField
            {
                Name = memberName,
                InitExpression = new CodePrimitiveExpression(value) // Uses key for value
            };

            _targetClass.Members.Add(member);
        }

        public void GenerateCSharpCode(string fileName)
        {
            var provider = CodeDomProvider.CreateProvider("CSharp");
            var options = new CodeGeneratorOptions
            {
                BracingStyle = "C"
            };

            Thread.Sleep(1000);

            if (!File.Exists(fileName))
            {
                try
                {
                    using (var sourceWriter = new StreamWriter(fileName))
                    {
                        provider.GenerateCodeFromCompileUnit(_targetUnit, sourceWriter, options);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }
    }

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

            Thread.Sleep(1000);
            if (!File.Exists(fileName))
            {
                try
                {
                    using (var sourceWriter = new StreamWriter(fileName))
                    {
                        provider.GenerateCodeFromCompileUnit(_targetUnit, sourceWriter, options);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
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
