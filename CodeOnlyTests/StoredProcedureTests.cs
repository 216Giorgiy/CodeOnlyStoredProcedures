﻿using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeOnlyStoredProcedure;
using System.Data.SqlClient;

namespace CodeOnlyTests
{
    [TestClass]
    public class StoredProcedureTests
    {
        private static readonly Type _basic = typeof(StoredProcedure);
        private static readonly Type _one   = typeof(StoredProcedure<>);
        private static readonly Type _two   = typeof(StoredProcedure<,>);
        private static readonly Type _three = typeof(StoredProcedure<,,>);
        private static readonly Type _four  = typeof(StoredProcedure<,,,>);
        private static readonly Type _five  = typeof(StoredProcedure<,,,,>);
        private static readonly Type _six   = typeof(StoredProcedure<,,,,,>);
        private static readonly Type _seven = typeof(StoredProcedure<,,,,,,>);

        private static readonly Type[] _generics = { _one, _two, _three, _four, _five, _six, _seven };

        #region Constructor Tests
        [TestMethod]
        public void TestConstructorSetsNameAndDefaultSchema()
        {
            TestConstructorWithDefaultSchema(_basic, "TestProc");

            for (int i = 0; i < 7; i++)
            {
                var type = _generics[i].MakeGenericType(Enumerable.Range(0, i + 1)
                                                                  .Select(_ => typeof(int))
                                                                  .ToArray());

                TestConstructorWithDefaultSchema(type, "TestProc" + i);
            }
        }

        [TestMethod]
        public void TestConstructorSetsNameAndSchema()
        {
            TestConstructorWithSchema(_basic, "tEst", "Proc");

            for (int i = 0; i < 7; i++)
            {
                var type = _generics[i].MakeGenericType(Enumerable.Range(0, i + 1)
                                                                  .Select(_ => typeof(int))
                                                                  .ToArray());

                TestConstructorWithSchema(type, "tEst", "TestProc" + i);
            }
        }

        private static void TestConstructorWithDefaultSchema(Type type, string name)
        {
            // this will actually call the SP's ctor
            var sp = (StoredProcedure)Activator.CreateInstance(type, name);
            AssertProcValues(sp, type, "dbo", name, 0, 0);
        }

        private static void TestConstructorWithSchema(Type type, string schema, string name)
        {
            // this will actually call the SP's ctor
            var sp = (StoredProcedure)Activator.CreateInstance(type, schema, name);
            AssertProcValues(sp, type, schema, name, 0, 0);
        }
        #endregion

        #region Create Tests
        [TestMethod]
        public void TestCreateSetsNameAndDefaultSchema()
        {
            var sp = StoredProcedure.Create("procMe");

            Assert.AreEqual("procMe", sp.Name);
            Assert.AreEqual("dbo", sp.Schema);
            Assert.AreEqual("[dbo].[procMe]", sp.FullName);
            Assert.AreEqual(0, sp.Parameters.Count());
            Assert.AreEqual(0, sp.OutputParameterSetters.Count());
        }

        [TestMethod]
        public void TestCreateSetsNameAndSchema()
        {
            var sp = StoredProcedure.Create("dummy", "usp_test");

            Assert.AreEqual("dummy", sp.Schema);
            Assert.AreEqual("usp_test", sp.Name);
            Assert.AreEqual("[dummy].[usp_test]", sp.FullName);
            Assert.AreEqual(0, sp.Parameters.Count());
            Assert.AreEqual(0, sp.OutputParameterSetters.Count());
        } 
        #endregion

        #region CloneWith Tests
        [TestMethod]
        public void TestCloneWithDoesNotAlterOriginalProcedure()
        {
            var sp = new StoredProcedure("test", "proc");

            var toTest = sp.CloneWith(new SqlParameter());

            Assert.AreEqual("test", sp.Schema);
            Assert.AreEqual("proc", sp.Name);
            Assert.AreEqual("[test].[proc]", sp.FullName);
            Assert.AreEqual(0, sp.Parameters.Count());
            Assert.AreEqual(0, sp.OutputParameterSetters.Count());
        }

        [TestMethod]
        public void TestCloneWithOutputParameterDoesNotAlterOriginalProcedure()
        {
            var sp = new StoredProcedure("test", "proc");

            var toTest = sp.CloneWith(new SqlParameter(), o => { });

            Assert.AreEqual("test", sp.Schema);
            Assert.AreEqual("proc", sp.Name);
            Assert.AreEqual("[test].[proc]", sp.FullName);
            Assert.AreEqual(0, sp.Parameters.Count());
            Assert.AreEqual(0, sp.OutputParameterSetters.Count());
        }

        [TestMethod]
        public void TestCloneWithRetainsNameAndDefaultSchema()
        {
            var sp = new StoredProcedure("test_proc");

            var toTest = sp.CloneWith(new SqlParameter());

            Assert.AreEqual("dbo", toTest.Schema);
            Assert.AreEqual("test_proc", toTest.Name);
            Assert.AreEqual("[dbo].[test_proc]", toTest.FullName);
            Assert.AreEqual(1, toTest.Parameters.Count());
            Assert.AreEqual(0, toTest.OutputParameterSetters.Count());
        }

        [TestMethod]
        public void TestCloneWithRetainsNameAndSchema()
        {
            var sp = new StoredProcedure("test", "proc");

            var toTest = sp.CloneWith(new SqlParameter());

            Assert.AreEqual("test", toTest.Schema);
            Assert.AreEqual("proc", toTest.Name);
            Assert.AreEqual("[test].[proc]", toTest.FullName);
            Assert.AreEqual(1, toTest.Parameters.Count());
            Assert.AreEqual(0, toTest.OutputParameterSetters.Count());
        }

        [TestMethod]
        public void TestCloneWithOutputParameterRetainsNameAndDefaultSchema()
        {
            var sp = new StoredProcedure("test_proc");

            var toTest = sp.CloneWith(new SqlParameter(), o => { });

            Assert.AreEqual("dbo", toTest.Schema);
            Assert.AreEqual("test_proc", toTest.Name);
            Assert.AreEqual("[dbo].[test_proc]", toTest.FullName);
            Assert.AreEqual(1, toTest.Parameters.Count());
            Assert.AreEqual(1, toTest.OutputParameterSetters.Count());
        }

        [TestMethod]
        public void TestCloneWithOutputParameterRetainsNameAndSchema()
        {
            var sp = new StoredProcedure("test", "proc");

            var toTest = sp.CloneWith(new SqlParameter(), o => { });

            Assert.AreEqual("test", toTest.Schema);
            Assert.AreEqual("proc", toTest.Name);
            Assert.AreEqual("[test].[proc]", toTest.FullName);
            Assert.AreEqual(1, toTest.Parameters.Count());
            Assert.AreEqual(1, toTest.OutputParameterSetters.Count());
        }

        [TestMethod]
        public void TestCloneWithStoresParameter()
        {
            var p = new SqlParameter { ParameterName = "Parm" };

            var toTest = new StoredProcedure("test").CloneWith(p);

            Assert.AreEqual(p, toTest.Parameters.Single());
        }

        [TestMethod]
        public void TestCloneWithStoresParameterAndSetter()
        {
            var p = new SqlParameter { ParameterName = "Parm" };
            var a = new Action<object>(o => { });

            var toTest = new StoredProcedure("test").CloneWith(p, a);

            Assert.AreEqual(p, toTest.Parameters.Single());
            var kv = toTest.OutputParameterSetters.Single();
            Assert.AreEqual("Parm", kv.Key);
            Assert.AreEqual(a, kv.Value);
        }
        #endregion

        private static void AssertProcValues(
            StoredProcedure proc,
            Type procType,
            string schema,
            string name,
            int parmCount, 
            int outputCount)
        {
            Assert.AreEqual(procType, proc.GetType());
            Assert.AreEqual(schema, proc.Schema);
            Assert.AreEqual(name, proc.Name);
            Assert.AreEqual(String.Format("[{0}].[{1}]", schema, name), proc.FullName);
            Assert.AreEqual(parmCount, proc.Parameters.Count());
            Assert.AreEqual(outputCount, proc.OutputParameterSetters.Count());
        }
    }
}
