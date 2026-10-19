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

# get abfs Path path to lakehouse in same workspace with display name of 'sales_silver'
bronze_lakehouse = notebookutils.lakehouse.get('sales_bronze')
bronze_lakehouse_path = bronze_lakehouse['properties']['abfsPath']

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

# get path for CVS file with bronze products data
products_cvs_file_path = f'{bronze_lakehouse_path}/Files/sales-data/products.csv'

print('Loading products table from /Files/sales-data/products.csv')

# create products table for silver layer
from pyspark.sql.types import StructType, StructField, StringType, LongType, FloatType

# create schema for products table using StructType and StructField 
schema_products = StructType([
    StructField("ProductId", LongType() ),
    StructField("Product", StringType() ),
    StructField("Category", StringType() )
])

# Load CSV file into Spark DataFrame and validate data using schema
df_products = (
    spark.read.format("csv")
         .option("header","true")
         .schema(schema_products)
         .load(products_cvs_file_path)
)

# save DataFrame as lakehouse table in Delta format
( df_products.write
             .mode("overwrite")
             .option("overwriteSchema", "True")
             .format("delta")
             .save("Tables/products")
)

# display table schema and data
df_products.printSchema()
df_products.show()

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

# get path for CVS file with bronze customers data
customers_cvs_file_path = f'{bronze_lakehouse_path}/Files/sales-data/customers.csv'

print('Loading customers table from /Files/sales-data/customers.csv')

# create customers table for silver layer
from pyspark.sql.types import StructType, StructField, StringType, LongType, DateType

# create schema for customers table using StructType and StructField 
schema_customers = StructType([
    StructField("CustomerId", LongType() ),
    StructField("FirstName", StringType() ),
    StructField("LastName", StringType() ),
    StructField("Country", StringType() ),
    StructField("City", StringType() ),
    StructField("DOB", DateType() ),
])

# Load CSV file into Spark DataFrame with schema and support to infer dates
df_customers = (
    spark.read.format("csv")
         .option("header","true")
         .schema(schema_customers)
         .option("dateFormat", "MM/dd/yyyy")
         .option("inferSchema", "true")
         .load(customers_cvs_file_path)
)

# save DataFrame as lakehouse table in Delta format
( df_customers.write
                .mode("overwrite")
                .option("overwriteSchema", "True")
                .format("delta")
                .save("Tables/customers")
)

# display table schema and data
df_customers.printSchema()
df_customers.show()

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

# get path for CVS file with bronze invoices data
invoices_cvs_file_path = f'{bronze_lakehouse_path}/Files/sales-data/invoices.csv'

print('Loading invoices table from /Files/sales-data/invoices.csv')

# create invoices table for silver layer
from pyspark.sql.types import StructType, StructField, LongType, FloatType, DateType

# create schema for invoices table using StructType and StructField 
schema_invoices = StructType([
    StructField("InvoiceId", LongType() ),
    StructField("Date", DateType() ),
    StructField("TotalSalesAmount", FloatType() ),
    StructField("CustomerId", LongType() )
])

# Load CSV file into Spark DataFrame with schema and support to infer dates
df_invoices = (
    spark.read.format("csv")
         .option("header","true")
         .schema(schema_invoices)
         .option("dateFormat", "MM/dd/yyyy")
         .option("inferSchema", "true") 
         .load(invoices_cvs_file_path)
)

# save DataFrame as lakehouse table in Delta format
( df_invoices.write
             .mode("overwrite")
             .option("overwriteSchema", "True")
             .format("delta")
             .save("Tables/invoices")
)

# display table schema and data
df_invoices.printSchema()
df_invoices.show()

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

# get path for CVS file with bronze invoice details data
invoice_details_cvs_file_path = f'{bronze_lakehouse_path}/Files/sales-data/invoicedetails.csv'

print('Loading invoices table from /Files/sales-data/invoicedetails.csv')

# create invoice_details table for silver layer
from pyspark.sql.types import StructType, StructField, LongType, FloatType

# create schema for invoice_details table using StructType and StructField 
schema_invoice_details = StructType([
    StructField("Id", LongType() ),
    StructField("Quantity", LongType() ),
    StructField("SalesAmount", FloatType() ),
    StructField("InvoiceId", LongType() ),
    StructField("ProductId", LongType() )
])

# Load CSV file into Spark DataFrame and validate data using schema
df_invoice_details = (
    spark.read.format("csv")
         .option("header","true")
         .schema(schema_invoice_details)
         .load(invoice_details_cvs_file_path)
)

# save DataFrame as lakehouse table in Delta format
( df_invoice_details.write
                    .mode("overwrite")
                    .option("overwriteSchema", "True")
                    .format("delta")
                    .save("Tables/invoice_details")
)

# display table schema and data
df_invoice_details.printSchema()
df_invoice_details.show()

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

# refresh SQL endpoint for all tables

spark.sql(f"REFRESH TABLE products")
spark.sql(f"REFRESH TABLE customers")
spark.sql(f"REFRESH TABLE invoices")
spark.sql(f"REFRESH TABLE invoice_details")

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }
