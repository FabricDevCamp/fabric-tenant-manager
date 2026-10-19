# Fabric notebook source

# METADATA ********************

# META {
# META   "kernel_info": {
# META     "name": "synapse_pyspark"
# META   },
# META   "dependencies": {
# META     "lakehouse": {
# META       "default_lakehouse": "1330d5c1-d173-94b9-438c-e716294575bd",
# META       "default_lakehouse_name": "sales_silver",
# META       "default_lakehouse_workspace_id": "00000000-0000-0000-0000-000000000000",
# META       "known_lakehouses": [
# META         {}
# META       ]
# META     }
# META   }
# META }

# CELL ********************

# read variable library item reference variable to determine gold lakehouse path
varlib = notebookutils.variableLibrary.getLibrary("environment_settings")

gold_lakehouse_ref = varlib.getVariable('gold_lakehouse')

gold_lakehouse_id = gold_lakehouse_ref.get('itemId').value()
gold_lakehouse_workspace_id = gold_lakehouse_ref.get('workspaceId').value()

gold_lakehouse_properties = notebookutils.lakehouse.get(gold_lakehouse_id, gold_lakehouse_workspace_id)
gold_lakehouse_path = gold_lakehouse_properties['properties']['abfsPath']
# gold_lakehouse_tables_path2 = f'abfss://{gold_lakehouse_workspace_id}@onelake.dfs.fabric.microsoft.com/{gold_lakehouse_id}/Tables'

print('gold_lakehouse_path:')
print(gold_lakehouse_path)

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

# create products table for gold layer

# (1) get path for gold products table
gold_products_table_path = f'{gold_lakehouse_path}/Tables/products'

# (2) load DataFrame from silver layer table
df_gold_products = (
    spark.read
         .format("delta")
         .load('Tables/products')
)

# (3) write DataFrame to new gold layer table 
( df_gold_products.write
                  .mode("overwrite")
                  .option("overwriteSchema", "True")
                  .format("delta")
                  .save(gold_products_table_path)
)

# (4) display table schema and data
df_gold_products.printSchema()
df_gold_products.show()

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

# create customers table for gold layer
from pyspark.sql.functions import concat_ws, floor, datediff, current_date, col

# (1) get path for gold customers table
gold_customers_table_path = f'{gold_lakehouse_path}/Tables/customers'

# (2) load DataFrame from silver layer table and perform transforms
df_gold_customers = (
    spark.read
         .format("delta")
         .load('Tables/customers')
         .withColumnRenamed("City", "CityName")
         .withColumn("City", concat_ws(', ', col('CityName'), col('Country')) )
         .withColumn("Customer", concat_ws(' ', col('FirstName'), col('LastName')) )
         .withColumn("Age",( floor( datediff( current_date(), col("DOB") )/365.25) ))   
         .drop('FirstName', 'LastName')
)

# write DataFrame to new gold layer table 
( df_gold_customers.write
                   .mode("overwrite")
                   .option("overwriteSchema", "True")
                   .format("delta")
                   .save(gold_customers_table_path)
)

# display table schema and data
df_gold_customers.printSchema()
df_gold_customers.show()

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

# create sales table for gold layer
from pyspark.sql.functions import col, desc, concat, lit, floor, datediff
from pyspark.sql.functions import date_format, to_date, current_date, year, month, dayofmonth

# (3) get path for gold sales table
gold_sales_table_path = f'{gold_lakehouse_path}/Tables/sales'

# load DataFrames using invoices table and invoice_details table from silver layer
df_silver_invoices = spark.read.format("delta").load('Tables/invoices')
df_silver_invoice_details = spark.read.format("delta").load('Tables/invoice_details')

# perform join to merge columns from both DataFrames and transform data 
df_gold_sales = (
    df_silver_invoice_details
        .join(df_silver_invoices, 
              df_silver_invoice_details['InvoiceId'] == df_silver_invoices['InvoiceId'])
        .withColumnRenamed('SalesAmount', 'Sales')
        .withColumn("DateKey", (year(col('Date'))*10000) + 
                               (month(col('Date'))*100) + 
                               (dayofmonth(col('Date')))   )
        .drop('InvoiceId', 'TotalSalesAmount', 'InvoiceId', 'Id')
        .select('Date', "DateKey", "CustomerId", "ProductId", "Sales", "Quantity")
)

# write DataFrame to new gold layer table 
( df_gold_sales.write
               .mode("overwrite")
               .option("overwriteSchema", "True")
               .format("delta")
               .save(gold_sales_table_path)
)

# display table schema and data
df_gold_sales.printSchema()
df_gold_sales.show()

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

# create calendar table for gold layer
from datetime import date
import pandas as pd
from pyspark.sql.functions import to_date, year, month, dayofmonth, quarter, dayofweek, date_format


# get path for gold calendar table
gold_calendar_table_path = f'{gold_lakehouse_path}/Tables/calendar'

# get first and last calendar date from sakes table 
first_sales_date = df_gold_sales.agg({"Date": "min"}).collect()[0][0]
last_sales_date = df_gold_sales.agg({"Date": "max"}).collect()[0][0]

# calculate start date and end date for calendar table
start_date = date(first_sales_date.year, 1, 1)
end_date = date(last_sales_date.year, 12, 31)

# create pandas DataFrame with Date series column
df_calendar_ps = pd.date_range(start_date, end_date, freq='D').to_frame()

# convert pandas DataFrame to Spark DataFrame and add calculated calendar columns
df_calendar_spark = (
     spark.createDataFrame(df_calendar_ps)
       .withColumnRenamed("0", "timestamp")
       .withColumn("Date", to_date(col('timestamp')))
       .withColumn("DateKey", (year(col('timestamp'))*10000) + 
                              (month(col('timestamp'))*100) + 
                              (dayofmonth(col('timestamp')))   )
       .withColumn("Year", year(col('timestamp'))  )
       .withColumn("Quarter", date_format(col('timestamp'),"yyyy-QQ")  )
       .withColumn("Month", date_format(col('timestamp'),'yyyy-MM')  )
       .withColumn("Day", dayofmonth(col('timestamp'))  )
       .withColumn("MonthInYear", date_format(col('timestamp'),'MMMM')  )
       .withColumn("MonthInYearSort", month(col('timestamp'))  )
       .withColumn("DayOfWeek", date_format(col('timestamp'),'EEEE')  )
       .withColumn("DayOfWeekSort", dayofweek(col('timestamp')))
       .drop('timestamp')
)

# write DataFrame to new gold layer table 
( df_calendar_spark.write
                   .mode("overwrite")
                   .option("overwriteSchema", "True")
                   .format("delta")
                   .save(gold_calendar_table_path)
)

# display table schema and data
df_calendar_spark.printSchema()
df_calendar_spark.show()

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }
