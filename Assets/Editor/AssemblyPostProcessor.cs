using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class AssemblyPostProcessor
{
    static AssemblyPostProcessor()
    {
        try
        {
            Debug.Log( "AssemblyPostProcessor running" );

            // Lock assemblies while they may be altered
            EditorApplication.LockReloadAssemblies();

            // This will hold the paths to all the assemblies that will be processed
            HashSet<string> assemblyPaths = new HashSet<string>();
            // This will hold the search directories for the resolver
            HashSet<string> assemblySearchDirectories = new HashSet<string>();

            // Add all assemblies in the project to be processed, and add their directory to
            // the resolver search directories.
            foreach( System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies() )
            {
                // Only process assemblies which are in the project
                if( assembly.Location.Replace( '\\', '/' ).StartsWith( Application.dataPath.Substring( 0, Application.dataPath.Length - 7 ) ) )
                {
                    assemblyPaths.Add( assembly.Location );
                }
                // But always add the assembly folder to the search directories
                assemblySearchDirectories.Add( Path.GetDirectoryName( assembly.Location ) );
            }

            // Create resolver
            DefaultAssemblyResolver assemblyResolver = new DefaultAssemblyResolver();
            // Add all directories found in the project folder
            foreach( String searchDirectory in assemblySearchDirectories )
            {
                assemblyResolver.AddSearchDirectory( searchDirectory );
            }
            // Add path to the Unity managed dlls
            assemblyResolver.AddSearchDirectory( Path.GetDirectoryName( EditorApplication.applicationPath ) + "/Data/Managed" );

            // Create reader parameters with resolver
            ReaderParameters readerParameters = new ReaderParameters();
            readerParameters.AssemblyResolver = assemblyResolver;

            // Create writer parameters
            WriterParameters writerParameters = new WriterParameters();

            // Process any assemblies which need it
            foreach( String assemblyPath in assemblyPaths )
            {
                // mdbs have the naming convention myDll.dll.mdb whereas pdbs have myDll.pdb
                String mdbPath = assemblyPath + ".mdb";
                String pdbPath = assemblyPath.Substring( 0, assemblyPath.Length - 3 ) + "pdb";

                // Figure out if there's an pdb/mdb to go with it
                if( File.Exists( pdbPath ) )
                {
                    readerParameters.ReadSymbols = true;
                    readerParameters.SymbolReaderProvider = new Mono.Cecil.Pdb.PdbReaderProvider();
                    writerParameters.WriteSymbols = true;
                    writerParameters.SymbolWriterProvider = new Mono.Cecil.Mdb.MdbWriterProvider(); // pdb written out as mdb, as mono can't work with pdbs
                }
                else if( File.Exists( mdbPath ) )
                {
                    readerParameters.ReadSymbols = true;
                    readerParameters.SymbolReaderProvider = new Mono.Cecil.Mdb.MdbReaderProvider();
                    writerParameters.WriteSymbols = true;
                    writerParameters.SymbolWriterProvider = new Mono.Cecil.Mdb.MdbWriterProvider();
                }
                else
                {
                    readerParameters.ReadSymbols = false;
                    readerParameters.SymbolReaderProvider = null;
                    writerParameters.WriteSymbols = false;
                    writerParameters.SymbolWriterProvider = null;
                }

                // Read assembly
                AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly( assemblyPath, readerParameters );
                
                // Process it if it hasn't already
                Debug.Log( "Processing " + Path.GetFileName( assemblyPath ) );
                if( AssemblyPostProcessor.ProcessAssembly( assemblyDefinition ) )
                {
                    Debug.Log( "Writing to " + assemblyPath );
                    assemblyDefinition.Write( assemblyPath, writerParameters );
                    Debug.Log( "Done writing" );
                }
                else
                {
                    Debug.Log( Path.GetFileName( assemblyPath ) + " didn't need to be processed" );
                }
            }

            // Unlock now that we're done
            EditorApplication.UnlockReloadAssemblies();
        }
        catch( Exception e )
        {
            Debug.LogWarning( e );
        }
    }

    private static bool ProcessAssembly( AssemblyDefinition assemblyDefinition )
    {
        bool wasProcessed = false;

        foreach( ModuleDefinition moduleDefinition in assemblyDefinition.Modules )
        {
            foreach( TypeDefinition typeDefinition in moduleDefinition.Types )
            {
                foreach( MethodDefinition methodDefinition in typeDefinition.Methods )
                {
                    CustomAttribute logAttribute = null;

                    foreach( CustomAttribute customAttribute in methodDefinition.CustomAttributes )
                    {
                        if( customAttribute.AttributeType.FullName == "LogAttribute" )
                        {
                            MethodReference logMethodReference = moduleDefinition.Import( typeof( Debug ).GetMethod( "Log", new Type[] { typeof( object ) } ) );

                            ILProcessor ilProcessor = methodDefinition.Body.GetILProcessor();

                            Instruction first = methodDefinition.Body.Instructions[0];
                            ilProcessor.InsertBefore( first, Instruction.Create( OpCodes.Ldstr, "Enter " + typeDefinition.FullName + "." + methodDefinition.Name ) );
                            ilProcessor.InsertBefore( first, Instruction.Create( OpCodes.Call, logMethodReference ) );

                            Instruction last = methodDefinition.Body.Instructions[methodDefinition.Body.Instructions.Count - 1];
                            ilProcessor.InsertBefore( last, Instruction.Create( OpCodes.Ldstr, "Exit " + typeDefinition.FullName + "." + methodDefinition.Name ) );
                            ilProcessor.InsertBefore( last, Instruction.Create( OpCodes.Call, logMethodReference ) );

                            wasProcessed = true;
                            logAttribute = customAttribute;
                            break;
                        }
                    }

                    // Remove the attribute so it won't be processed again
                    if( logAttribute != null )
                    {
                        methodDefinition.CustomAttributes.Remove( logAttribute );
                    }
                }
            }
        }
        
        return wasProcessed;
    }
}
