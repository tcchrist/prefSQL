﻿using System;
using System.Configuration;
using System.Data;
using System.Data.Common;

namespace Utility
{
    class Helper
    {
        public static string ConnectionString = ConfigurationManager.ConnectionStrings["localhost"].ConnectionString;
        public static string ProviderName = ConfigurationManager.ConnectionStrings["localhost"].ProviderName;


       


        public static DataTable ExecuteStatement(String strSQL)
        {
            DataTable dt = new DataTable();

            //Generic database provider
            //Create the provider factory from the namespace provider, you could create any other provider factory.. for Oracle, MySql, etc...
            DbProviderFactory factory = DbProviderFactories.GetFactory(ProviderName);

            // use the factory object to create Data access objects.
            DbConnection connection = factory.CreateConnection(); // will return the connection object, in this case, SqlConnection ...
            if (connection != null)
            {
                connection.ConnectionString = ConnectionString;

                connection.Open();
                DbCommand command = connection.CreateCommand();
                command.CommandTimeout = 0; //infinite timeout
                command.CommandText = strSQL;
                DbDataAdapter db = factory.CreateDataAdapter();
                if (db != null)
                {
                    db.SelectCommand = command;
                    db.Fill(dt);
                }
            }

            return dt;
        }
    }
}
