using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using Microsoft.SqlServer.Server;

//------------------------------------------------------------------------------
// <copyright file="CSSqlClassFile.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace prefSQL.SQLSkyline
{
    using System.Collections.ObjectModel;

    /// <summary>
    /// 
    /// 
    /// </summary>
    /// <remarks>
    /// Profiling considersations:
    /// - Don't use getUpperBound inside a performanc critical method (i.e. IsTupleDominated) --> slows down performance
    /// </remarks>
    class Helper
    {
        //Only this parameters are different beteen SQL CLR function and Utility class
        public const string CnnStringSqlclr = "context connection=true";
        public const string ProviderClr = "System.Data.SqlClient";

        public static DataTable executeSQL(string strQuery, string strConnection, string strProvider) {

            DataTable dt = new DataTable();
            DbProviderFactory factory = DbProviderFactories.GetFactory(strProvider);

            // use the factory object to create Data access objects.
            DbConnection connection = factory.CreateConnection(); // will return the connection object (i.e. SqlConnection ...)
            if (connection != null)
            {
                connection.ConnectionString = strConnection;

                try
                {
                    connection.Open();

                    DbDataAdapter dap = factory.CreateDataAdapter();
                    DbCommand selectCommand = connection.CreateCommand();
                    selectCommand.CommandTimeout = 0; //infinite timeout
                    selectCommand.CommandText = strQuery;

                    if (dap != null)
                    {
                        dap.SelectCommand = selectCommand;
                        dap.Fill(dt);
                    }
                }
                catch (Exception e)
                {
                    throw new Exception(e.Message);
                }
                finally
                {
                    connection.Close();
                }


            }
            return dt;
        }

        public static List<object[]> GetObjectArrayFromSQLWithLevel(string strQuery, string strConnection, string strProvider, DataTable dt, string[] operators, out SqlDataRecord record)
        {
            return GetObjectArrayFromSQL(strQuery, strConnection, strProvider, dt, operators, out record, true);
        }

        public static List<object[]> GetObjectArrayFromSQL(string strQuery, string strConnection, string strProvider, DataTable dt, string[] operators, out SqlDataRecord record)
        {
            return GetObjectArrayFromSQL(strQuery, strConnection, strProvider, dt, operators, out record, false);
        }

        private static List<object[]> GetObjectArrayFromSQL(string strQuery, string strConnection, string strProvider, DataTable dt, string[] operators, out SqlDataRecord record, bool addLevel)
        {
            record = null;
            List<object[]> listObjects = new List<object[]>();
            DbProviderFactory factory = DbProviderFactories.GetFactory(strProvider);

            // use the factory object to create Data access objects.
            DbConnection connection = factory.CreateConnection(); // will return the connection object (i.e. SqlConnection ...)
            if (connection != null)
            {
                connection.ConnectionString = strConnection;

                try
                {
                    connection.Open();

                    DbDataAdapter dap = factory.CreateDataAdapter();
                    DbCommand selectCommand = connection.CreateCommand();
                    selectCommand.CommandTimeout = 0; //infinite timeout
                    selectCommand.CommandText = strQuery;

                    if (dap != null)
                    {
                        DbDataReader reader = selectCommand.ExecuteReader();
                        List<SqlMetaData> outputColumns = new List<SqlMetaData>();
                        object[] recordObjectStart = new object[reader.FieldCount];

                        //only if data is available
                        if (reader.Read())
                        {
                            for (int iCol = 0; iCol < reader.FieldCount; iCol++)
                            {
                                recordObjectStart[iCol] = (object)reader[iCol];

                                if (iCol >= operators.Length)
                                {
                                    DataColumn col = new DataColumn(reader.GetName(iCol), reader.GetFieldType(iCol));

                                    SqlMetaData outputColumn;
                                    if (col.DataType == typeof(Int32) || col.DataType == typeof(Int64) || col.DataType == typeof(DateTime))
                                    {
                                        outputColumn = new SqlMetaData(col.ColumnName, TypeConverter.ToSqlDbType(col.DataType));
                                    }
                                    else
                                    {
                                        outputColumn = new SqlMetaData(col.ColumnName, TypeConverter.ToSqlDbType(col.DataType), col.MaxLength);
                                    }
                                    outputColumns.Add(outputColumn);

                                    //Check if column name already exists
                                    if (!dt.Columns.Contains(col.ColumnName))
                                    {
                                        dt.Columns.Add(col);
                                    }
                                    else
                                    {
                                        throw new Exception("Column name '" + col.ColumnName + "' already exists. Use an alias instead.");
                                    }
                                
                                }
                            }
                        }
                        
                        listObjects.Add(recordObjectStart);

                        //add level column for multiple skyline algorithms
                        if (addLevel)
                        {
                            SqlMetaData outputColumnLevel = new SqlMetaData("level", TypeConverter.ToSqlDbType(typeof(Int32)));
                            outputColumns.Add(outputColumnLevel);
                        }
                        

                        record = new SqlDataRecord(outputColumns.ToArray());
                        //Now save all records to array (Profiling: faster than working with the reader in the algorithms)
                        while (reader.Read())
                        {
                            object[] recordObject = new object[reader.FieldCount];
                            for (int iCol = 0; iCol < reader.FieldCount; iCol++)
                            {
                                recordObject[iCol] = (object)reader[iCol];

                            }
                            listObjects.Add(recordObject);
                        }
                        reader.Close();

                    }
                }
                catch (Exception e)
                {
                    throw e;
                }
                finally
                {
                    connection.Close();
                }


            }
            return listObjects;
        }

        /// <summary>
        /// Returns the TOP n first tupels of a datatable
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="numberOfRecords"></param>
        /// <returns></returns>
        public static DataTable GetAmountOfTuples(DataTable dt, int numberOfRecords)
        {
            if (numberOfRecords > 0)
            {
                for (int i = dt.Rows.Count - 1; i >= numberOfRecords; i--)
                {
                    dt.Rows.RemoveAt(i);
                }

            }
            return dt;
        }

        /// <summary>
        /// Adds every output column to a new datatable and creates the structure to return data over MSSQL CLR pipes
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="operators"></param>
        /// <param name="dtSkyline"></param>
        /// <returns></returns>
        public static List<SqlMetaData> BuildRecordSchema(DataTable dt, string[] operators, DataTable dtSkyline)
        {
            List<SqlMetaData> outputColumns = new List<SqlMetaData>(dt.Columns.Count - (operators.Length));
            int iCol = 0;
            foreach (DataColumn col in dt.Columns)
            {
                //Only the real columns (skyline columns are not output fields)
                if (iCol >= operators.Length)
                {
                    SqlMetaData outputColumn;
                    if (col.DataType == typeof(Int32) || col.DataType == typeof(Int64) || col.DataType == typeof(DateTime))
                    {
                        outputColumn = new SqlMetaData(col.ColumnName, TypeConverter.ToSqlDbType(col.DataType));
                    }
                    else
                    {
                        outputColumn = new SqlMetaData(col.ColumnName, TypeConverter.ToSqlDbType(col.DataType), col.MaxLength);
                    }
                    outputColumns.Add(outputColumn);
                    dtSkyline.Columns.Add(col.ColumnName, col.DataType);
                }
                iCol++;
            }
            return outputColumns;
        }

        /// <summary>
        /// Adds every output column to a new datatable and creates the structure to return data over MSSQL CLR pipes
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="operators"></param>
        /// <param name="dtSkyline"></param>
        /// <returns></returns>
        public static SqlDataRecord BuildDataRecord(DataTable dt, string[] operators, DataTable dtSkyline)
        {           
            List<SqlMetaData> outputColumns = BuildRecordSchema(dt, operators, dtSkyline);
            return new SqlDataRecord(outputColumns.ToArray());
        }


        /// <summary>
        /// Compares a tuple against another tuple according to preference logic. Cannot handle incomparable values
        /// Better values are smaller!
        /// </summary>
        /// <returns></returns>
        public static bool IsTupleDominated(long[] windowTuple, long[] newTuple, int[] dimensions)
        {
            bool greaterThan = false;
            int nextComparisonIndex = 0;

            foreach (int iCol in dimensions)
            {
                //Profiling
                //Use explicit conversion (long)dataReader[iCol] instead of dataReader.GetInt64(iCol) is 20% faster!
                //Use long array instead of dataReader --> is 100% faster!!!
                //long value = dataReader.GetInt64(iCol);
                //long value = (long)dataReader[iCol];
                //long value = tupletoCheck[iCol].Value;
                long value = newTuple[nextComparisonIndex]; //.Value;
                long tmpValue = windowTuple[nextComparisonIndex];

                if (value < tmpValue)
                {
                    return false;
                }

                if (value > tmpValue)
                {
                    //at least one must be greater than
                    greaterThan = true;
                }            

                nextComparisonIndex++;
            }


            //all equal and at least one must be greater than
            return greaterThan;

        }


        /// <summary>
        /// Same function as isTupleDominated, but values are interchanged
        /// 
        /// </summary>
        /// <param name="windowTuple"></param>
        /// <param name="newTuple"></param>
        /// <param name="dimensions"></param>
        /// <returns></returns>
        public static bool DoesTupleDominate(long[] windowTuple, long[] newTuple, int[] dimensions)
        {
            bool greaterThan = false;
            int nextComparisonIndex = 0;

            foreach (int iCol in dimensions)
            {
                //Use long array instead of dataReader --> is 100% faster!!!
                long value = newTuple[nextComparisonIndex];
                long tmpValue = windowTuple[nextComparisonIndex];

                //interchange values for comparison                
                if (tmpValue < value)
                {
                    return false;
                }

                if (tmpValue > value)
                {
                    //at least one must be greater than
                    greaterThan = true;
                }           

                nextComparisonIndex++;
            }

            //all equal and at least one must be greater than
            //if (equalTo == true && greaterThan == true)
            return greaterThan;
        }


        /// <summary>
        /// Compares a tuple against another tuple according to preference logic. Can handle incomparable values
        /// Better values are smaller!
        /// </summary>
        /// <param name="dimensions"></param>
        /// <param name="operators"></param>
        /// <param name="windowTuple"></param>
        /// <param name="newTuple"></param>
        /// <param name="resultIncomparable"></param>
        /// <param name="newTupleAllValues"></param>
        /// <returns></returns>
        public static bool IsTupleDominated(long[] windowTuple, long[] newTuple, int[] dimensions, string[] operators, string[] resultIncomparable, object[] newTupleAllValues)
        {
            bool greaterThan = false;
            int nextComparisonIndex = 0;

            foreach (int iCol in dimensions)
            {
                string op = operators[iCol];
                //Compare only LOW attributes
                if (op.Equals("LOW"))
                {
                    long value = newTuple[nextComparisonIndex];
                    long tmpValue = windowTuple[nextComparisonIndex];

                    if (value < tmpValue)
                    {
                        //Value is smaller --> return false
                        return false;
                    }

                    if (value > tmpValue)
                    {
                        //at least one must be greater than
                        greaterThan = true;
                    }
                    else
                    {
                        //It is the same long value
                        //Check if the value must be text compared
                        if (iCol + 1 < operators.Length && operators[iCol + 1].Equals("INCOMPARABLE"))
                        {
                            //string value is always the next field
                            var strValue = (string) newTupleAllValues[iCol + 1];
                            //If it is not the same string value, the values are incomparable!!
                            //If two values are comparable the strings will be empty!
                            if (strValue.Equals("INCOMPARABLE") ||
                                !strValue.Equals(resultIncomparable[nextComparisonIndex]))
                            //TODO: check if strValue.equals incomparable is necessary... if (!strValue.Equals(resultIncomparable[iCol]))
                            {
                                //Value is incomparable --> return false
                                return false;
                            }
                        }
                    }               

                    nextComparisonIndex++;
                }
            }


            //all equal and at least one must be greater than
            return greaterThan;

        }

        /// <summary>
        /// Same function as isTupleDominate, but values are interchanged
        /// </summary>
        /// <param name="dimensions"></param>
        /// <param name="operators"></param>
        /// <param name="stringResult"></param>
        /// <param name="windowTuple"></param>
        /// <param name="newTuple"></param>
        /// <param name="newTupleAllValues"></param>
        /// <returns></returns>
        public static bool DoesTupleDominate(long[] windowTuple, long[] newTuple, int[] dimensions, string[] operators, string[] stringResult, object[] newTupleAllValues)
        {
            bool greaterThan = false;
            int nextComparisonIndex = 0;

            foreach (int iCol in dimensions)
            {             
                string op = operators[iCol];
                //Compare only LOW attributes
                if (op.Equals("LOW"))
                {
                    long value = newTuple[nextComparisonIndex];
                    long tmpValue = windowTuple[nextComparisonIndex];

                    //interchange values for comparison
                    if (tmpValue < value)
                    {
                        return false;
                    }

                    if (tmpValue > value)
                    {
                        //at least one must be greater than
                        greaterThan = true;
                    }
                    else
                    {
                        //It is the same long value
                        //Check if the value must be text compared
                        if (iCol + 1 < operators.Length && operators[iCol + 1].Equals("INCOMPARABLE"))
                        {
                            //string value is always the next field
                            var strValue = (string) newTupleAllValues[iCol + 1];
                            //If it is not the same string value, the values are incomparable!!
                            //If two values are comparable the strings will be empty!
                            if (!strValue.Equals(stringResult[nextComparisonIndex]))
                            {
                                //Value is incomparable --> return false
                                return false;
                            }
                        }
                    }                 

                    nextComparisonIndex++;
                }                
            }

            //all equal and at least one must be greater than
            //if (equalTo == true && greaterThan == true)
            return greaterThan;
        }

        /// <summary>
        /// Adds a tuple to the existing window. cannot handle incomparable values
        /// </summary>
        /// <param name="newTuple"></param>
        /// <param name="window"></param>
        /// <param name="operators"></param>
        /// <param name="dtResult"></param>
        public static void AddToWindow(object[] newTuple, List<long[]> window, string[] operators, DataTable dtResult)
        {
            long[] record = new long[operators.Count(op => op != "IGNORE" && op != "INCOMPARABLE")];
            int nextRecordIndex = 0;
            DataRow row = dtResult.NewRow();

            for (int iCol = 0; iCol < newTuple.Length; iCol++)
            {
                //Only the real columns (skyline columns are not output fields)
                if (iCol < operators.Length)
                {
                    //IGNORE is used for sample skyline. Only attributes that are not ignored shold be tested
                    if (operators[iCol] == "IGNORE")
                    {
                        continue;
                    }

                    record[nextRecordIndex] = (long) newTuple[iCol];
                    nextRecordIndex++;
                }
                else
                {
                    row[iCol - operators.Length] = newTuple[iCol];
                }
            }


            //DataTable is for the returning values
            dtResult.Rows.Add(row);
            //Window contains the skyline values (for the algorithm)
            window.Add(record);

        }

        /// <summary>
        /// Adds a tuple to the existing window. Can handle incomparable values
        /// </summary>
        /// <param name="newTuple"></param>
        /// <param name="operators"></param>
        /// <param name="window"></param>
        /// <param name="resultstringCollection"></param>
        /// <param name="dtResult"></param>
        public static void AddToWindowIncomparable(object[] newTuple, List<long[]> window, string[] operators, ArrayList resultstringCollection, DataTable dtResult)
        {
            //long must be nullable (because of incomparable tupels)
            long[] recordInt = new long[operators.Count(op => op != "IGNORE" && op != "INCOMPARABLE")];
            string[] recordstring = new string[operators.Count(op => op != "IGNORE" && op != "INCOMPARABLE")];
            int nextRecordIndex = 0;
            DataRow row = dtResult.NewRow();

            for (int iCol = 0; iCol < newTuple.Length; iCol++)
            {
                //Only the real columns (skyline columns are not output fields)
                if (iCol < operators.Length)
                {
                    //IGNORE is used for sample skyline. Only attributes that are not ignored shold be tested
                    if (operators[iCol] == "IGNORE")
                    {                       
                        continue;
                    }

                    string op = operators[iCol];

                    //LOW und HIGH Spalte in record abf�llen
                    if (op.Equals("LOW"))
                    {
                        recordInt[nextRecordIndex] = (long) newTuple[iCol];

                        //Check if long value is incomparable
                        if (iCol + 1 < operators.Length && operators[iCol + 1].Equals("INCOMPARABLE"))
                        {
                            //Incomparable field is always the next one
                            recordstring[nextRecordIndex] = (string) newTuple[iCol + 1];
                        }

                        nextRecordIndex++;
                    }                 
                }
                else
                {
                    row[iCol - operators.Length] = newTuple[iCol];
                }
            }         

            dtResult.Rows.Add(row);
            window.Add(recordInt);
            resultstringCollection.Add(recordstring);
        }

        /// <summary>
        ///     Gets the ItemArray from each row of dataTable and converts it to a List.
        /// </summary>
        /// <remarks>
        ///     Implemented as a bulk operation, this is faster than conversion by iterating over each row of dataTable.
        /// </remarks>
        /// <param name="dataTable">The DataTable from which the rows resp. ItemArrays are extracted.</param>
        /// <returns>A List containing the ItemArrays of each row of dataTable.</returns>
        public static List<object[]> GetItemArraysAsList(DataTable dataTable)
        {
            //Write all attributes to a Object-Array
            //Profiling: This is much faster (factor 2) than working with the SQLReader
            return dataTable.Rows.Cast<DataRow>().Select(dataRow => dataRow.ItemArray).ToList();
        }

        /// <summary>
        ///     Produces a Collection by which a dataTable's row can be accessed via its unique ID existing in the column specified
        ///     by uniqueIdColumnIndex.
        /// </summary>
        /// <remarks>
        /// Delegates to <see cref="GetDatabaseAccessibleByUniqueId(System.Data.DataTable,int,bool)"/> with the last parameter, fillUniqueIdColumn, set to false.
        /// </remarks>
        /// <param name="dataTable">The DataTable from which the rows resp. ItemArrays are extracted.</param>
        /// <param name="uniqueIdColumnIndex">
        ///     The index of the column containing a unique ID. The type of this column has to be
        ///     typeof(long).
        /// </param>
        /// <returns>A Collection by which a row can be accessed via its unique ID.</returns>
        public static IReadOnlyDictionary<long, object[]> GetDatabaseAccessibleByUniqueId(DataTable dataTable,
            int uniqueIdColumnIndex)
        {
            return GetDatabaseAccessibleByUniqueId(dataTable, uniqueIdColumnIndex, false);
        }

        /// <summary>
        ///     Produces a Collection by which a dataTable's row can be accessed via its unique ID existing in the column specified
        ///     by uniqueIdColumnIndex.
        /// </summary>
        /// <param name="dataTable">The DataTable from which the rows resp. ItemArrays are extracted.</param>
        /// <param name="uniqueIdColumnIndex">
        ///     The index of the column containing a unique ID. The type of this column has to be
        ///     typeof(long).
        /// </param>
        /// <param name="fillUniqueIdColumn">
        ///     Specifies whether to create artificial unique identifiers for each row and fill them into the column at position
        ///     uniqueIdColumnIndex or not (i.e., the column already is filled, e.g., when using a already SELECTed unique column).
        /// </param>
        /// <returns>A Collection by which a row can be accessed via its unique ID.</returns>
        public static IReadOnlyDictionary<long, object[]> GetDatabaseAccessibleByUniqueId(DataTable dataTable,
            int uniqueIdColumnIndex, bool fillUniqueIdColumn)
        {
            IDictionary<long, object[]> ret = null;

            if (fillUniqueIdColumn)
            {
                ret = new Dictionary<long, object[]>();

                long count = 0;
                foreach (DataRow row in dataTable.Rows)
                {
                    object[] itemArray = row.ItemArray;
                    itemArray[uniqueIdColumnIndex] = count;
                    ret.Add(count, itemArray);
                    count++;
                }
            }
            else
            {
                // Convert.ToInt64 because type of column might be int (e.g., when selecting an ID column of type int and using this column as uniqueIdColumnIndex)
                ret = dataTable.Rows.Cast<DataRow>()
                    .ToDictionary(row => Convert.ToInt64(row[uniqueIdColumnIndex]), row => row.ItemArray);
            }

            return new ReadOnlyDictionary<long, object[]>(ret);
        }

        public static DataTable GetDataTableFromSQL(string strQuery, string strConnection, string strProvider)
        {
            DbProviderFactory factory = DbProviderFactories.GetFactory(strProvider);
            DataTable dt = new DataTable();
            // use the factory object to create Data access objects.
            DbConnection connection = factory.CreateConnection(); // will return the connection object (i.e. SqlConnection ...)
            if (connection != null)
            {
                connection.ConnectionString = strConnection;

                try
                {
                    connection.Open();

                    DbDataAdapter dap = factory.CreateDataAdapter();
                    DbCommand selectCommand = connection.CreateCommand();
                    selectCommand.CommandTimeout = 0; //infinite timeout
                    selectCommand.CommandText = strQuery;
                    if (dap != null)
                    {
                        dap.SelectCommand = selectCommand;
                        dt = new DataTable();

                        dap.Fill(dt);
                    }
                }
                catch (Exception e)
                {
                    throw new Exception(e.Message);
                }
                finally
                {
                    connection.Close();
                }

                
            }
            return dt;
        }   

        //Sort BySum (for algorithms)
        public static DataTable SortBySum(DataTable dt, List<long[]> skylineValues)
        {
            //Add a column for each skyline attribute and a sort column
            long[] firstSkylineValues = skylineValues[0];
            int preferences = firstSkylineValues.GetUpperBound(0);

            for (int i = 0; i <= preferences; i++)
            {
                dt.Columns.Add("Skyline" + i, typeof(long));
            }
            dt.Columns.Add("SortOrder", typeof(int));

            //Add values to datatable
            for (int iRow = 0; iRow < dt.Rows.Count; iRow++)
            {
                long[] values = skylineValues[iRow];
                for (int i = 0; i <= preferences; i++)
                {
                    dt.Rows[iRow]["Skyline" + i] = values[i];
                }
            }
            dt = dt.DefaultView.ToTable();
            int preferenceStart = dt.Columns.Count - preferences - 2;

            //Now sort the table for each skyline table and calculate sortorder
            for (int iCol = preferenceStart; iCol < dt.Columns.Count - 1; iCol++)
            {
                //Sort by column and work with sorted table
                dt.DefaultView.Sort = dt.Columns[iCol].ColumnName + " ASC";
                dt = dt.DefaultView.ToTable();

                //Now replace values beginning from 0
                //int value = (int)dtResult.Rows[0][iCol];
                long rank = 0;
                long value = (long)dt.Rows[0][iCol];
                for (int iRow = 0; iRow < dt.Rows.Count; iRow++)
                {
                    if (value < (long)dt.Rows[iRow][iCol])
                    {
                        value = (long)dt.Rows[iRow][iCol];
                        rank++;
                    }
                    
                    if (dt.Rows[iRow]["SortOrder"] == DBNull.Value)
                    {
                        dt.Rows[iRow]["SortOrder"] = rank;
                    }
                    else
                    {
                        dt.Rows[iRow]["SortOrder"] = (int)dt.Rows[iRow]["SortOrder"] + rank;
                    }

                }
            }
            dt.DefaultView.Sort = "SortOrder ASC";
            dt = dt.DefaultView.ToTable();

            //Remove rows
            for (int i = 0; i <= preferences; i++)
            {
                dt.Columns.Remove("Skyline" + i);
            }
            dt.Columns.Remove("SortOrder");

            return dt;
        }


        //Sort ByRank (for algorithms)
        public static DataTable SortByRank(DataTable dt, List<long[]> skylineValues)
        {
            //Add a column for each skyline attribute and a sort column
            long[] firstSkylineValues = skylineValues[0];
            int preferences = firstSkylineValues.GetUpperBound(0);
            
            for (int i = 0; i <= preferences; i++)
            {
                dt.Columns.Add("Skyline" + i, typeof(long));
            }
            dt.Columns.Add("SortOrder", typeof(int));

            //Add values to datatable
            for(int iRow = 0; iRow < dt.Rows.Count; iRow++) {
                long[] values = skylineValues[iRow];
                for (int i = 0; i <= preferences; i++)
                {
                    dt.Rows[iRow]["Skyline" + i] = values[i];
                }
            }
            dt = dt.DefaultView.ToTable();
            int preferenceStart = dt.Columns.Count - preferences - 2;
            
            //Now sort the table for each skyline table and calculate sortorder
            for (int iCol = preferenceStart; iCol < dt.Columns.Count - 1; iCol++)
            {
                //Sort by column and work with sorted table
                dt.DefaultView.Sort = dt.Columns[iCol].ColumnName + " ASC";
                dt = dt.DefaultView.ToTable();

                //Now replace values beginning from 0
                //int value = (int)dtResult.Rows[0][iCol];
                long rank = 0;
                for (int iRow = 0; iRow < dt.Rows.Count; iRow++)
                {
                    rank++;
                    if (dt.Rows[iRow]["SortOrder"] == DBNull.Value || rank < (int)dt.Rows[iRow]["SortOrder"])
                    {
                        dt.Rows[iRow]["SortOrder"] = rank;
                    }

                }
            }
            dt.DefaultView.Sort = "SortOrder ASC";
            dt = dt.DefaultView.ToTable();

            //Remove rows
            for (int i = 0; i <= preferences; i++)
            {
                dt.Columns.Remove("Skyline" + i);
            }
            dt.Columns.Remove("SortOrder");

            return dt;
        }

        public static void SendDataTableOverPipe(DataTable tbl)
        {
            // Build our record schema 
            List<SqlMetaData> OutputColumns = new List<SqlMetaData>(tbl.Columns.Count);
            foreach (DataColumn col in tbl.Columns)
            {

                SqlMetaData outputColumn;
                if (col.DataType == typeof(Int32) || col.DataType == typeof(Int64) || col.DataType == typeof(DateTime))
                {
                    outputColumn = new SqlMetaData(col.ColumnName, TypeConverter.ToSqlDbType(col.DataType));
                }
                else if (col.DataType == typeof(Decimal))
                {
                    outputColumn = new SqlMetaData(col.ColumnName, TypeConverter.ToSqlDbType(col.DataType), 12, 10);
                }
                else
                {
                    outputColumn = new SqlMetaData(col.ColumnName, TypeConverter.ToSqlDbType(col.DataType), col.MaxLength);
                }

                OutputColumns.Add(outputColumn);
            }

            // Build our SqlDataRecord and start the results 
            SqlDataRecord record = new SqlDataRecord(OutputColumns.ToArray());
            SqlContext.Pipe.SendResultsStart(record);

            // Now send all the rows 
            foreach (DataRow row in tbl.Rows)
            {
                for (int col = 0; col < tbl.Columns.Count; col++)
                {
                    record.SetValue(col, row.ItemArray[col]);
                }
                SqlContext.Pipe.SendResultsRow(record);
            }

            // And complete the results 
            SqlContext.Pipe.SendResultsEnd();
        }
    }
}
