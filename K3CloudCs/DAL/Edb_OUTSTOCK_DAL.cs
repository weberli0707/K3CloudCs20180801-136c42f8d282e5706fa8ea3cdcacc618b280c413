using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;

namespace K3CloudCs
{
    public class Edb_OUTSTOCK_DAL
    {
        private string strcon;
        string date_type = "";
        string page_size = "";
        string order_status = "";
        List<string> dtTrade;
        List<string> dtTradeEntry;
        ConfigHelper config;
        public Edb_OUTSTOCK_DAL(string Constring,string strpage_size, string strdate_type,string strorder_status)
        {
            config = new ConfigHelper();
            strcon = Constring;
            page_size = strpage_size;
            date_type = strdate_type;
            order_status = strorder_status;
            dtTrade = GetColumnNames("tb_Trade");
            dtTradeEntry = GetColumnNames("tb_TradeEntry");
        }
        private List<string> GetColumnNames(string TableName)
        {
            List<string> ColunmNames = new List<string>();
            string strSQL = "select * from [dbo].["+ TableName + "] where 1=2";
            DataTable dt = SqlHelper.GetDataTable(strcon, strSQL, CommandType.Text, null);
            foreach (DataColumn dc in dt.Columns)
            {
                ColunmNames.Add(dc.ColumnName);
            }
            return ColunmNames;
        }
        private void SqlBulkCopyInsert(string strTableName, DataTable dtData)
        {
            try
            {
                using (SqlBulkCopy sqlRevdBulkCopy = new SqlBulkCopy(strcon))//引用SqlBulkCopy  
                {
                    sqlRevdBulkCopy.DestinationTableName = strTableName;//数据库中对应的表名  
                    sqlRevdBulkCopy.NotifyAfter = dtData.Rows.Count;//有几行数据  
                    sqlRevdBulkCopy.WriteToServer(dtData);//数据导入数据库  
                    sqlRevdBulkCopy.Close();//关闭连接  
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteFileLog(typeof(Edb_OUTSTOCK_DAL), ex.ToString());
            }
        }	
	    private void StringToDB(string begin_time,string end_time,bool flag)
	    {
            List<SqlParameter> pars = new List<SqlParameter>();            
            pars.Add(new SqlParameter("@begin_time",begin_time));
            pars.Add(new SqlParameter("@end_time", end_time));
            pars.Add(new SqlParameter("@flag", flag));
            int i = SqlHelper.ExecuteNonQuery(strcon, CommandType.Text, "Delete from tb_JsonStream where flag=1 or (begin_time=@begin_time and end_time=@end_time);Insert Into tb_JsonStream(begin_time,end_time,flag) values (@begin_time,@end_time,@flag)", pars.ToArray());

	    }
        
        private DataTable JsonToDataTable(string sJson)
        {
            try
            {
                DataTable api_Table = JsonConvert.DeserializeObject(sJson, typeof(DataTable)) as DataTable;
                return api_Table;
            }
            catch
            {
                return null;
            }
        }
        private string GetJson(DataTable dt)
        {
            try
            {
                string sJson = JsonConvert.SerializeObject(dt);
                return sJson;
            }
            catch
            {
                return string.Empty;
            }
        }
        public void DownloadErrorOutToDB(string StrserviceUrl, string Strdbhost, string Strappkey, string Strappscret, string Strtoken)
        {
            object o = SqlHelper.ExecuteScalar(strcon, CommandType.Text, "select count(id) from tb_JsonStream where flag=0");
            if (o != null)
            {
                using (SqlDataReader sdr = SqlHelper.ExecuteReader(strcon, CommandType.Text, "select * from (select distinct begin_time,end_time from tb_JsonStream where flag=0 ) t order by begin_time"))
                {
                    while (sdr.Read())
                    {
                        string begin_time = sdr["begin_time"].ToString();
                        string end_time = sdr["end_time"].ToString();
                        LoadInfo(date_type, begin_time, end_time, order_status, StrserviceUrl, Strdbhost, Strappkey, Strappscret, Strtoken);
                    }
                }                
            }
        }
        public void DownloadOutToDB(string StrserviceUrl, string Strdbhost, string Strappkey, string Strappscret, string Strtoken)
        {
            object strdt1 = SqlHelper.ExecuteScalar(strcon, CommandType.Text, "exec p_edb_GetDate");
            try
            {
                DateTime dt1 = Convert.ToDateTime(strdt1.ToString());
                TimeSpan span = DateTime.Now- dt1;
                if (span.TotalHours> 1)
                {
                    DateTime dt2 = dt1.AddDays(1);
                    LoadInfo(date_type, dt1.ToString("yyyy-MM-dd HH:mm:ss"), dt2.ToString("yyyy-MM-dd HH:mm:ss"), order_status, StrserviceUrl, Strdbhost, Strappkey, Strappscret, Strtoken);                   
                }
            }               
            catch(Exception ex)
            {
                LogHelper.WriteFileLog(typeof(Edb_OUTSTOCK_DAL), ex.ToString());
            }
        } 
        private void LoadInfo(string date_type,string begin_time,string end_time,string order_status, string StrserviceUrl, string Strdbhost, string Strappkey, string Strappscret, string Strtoken)
        {
            bool isSuccess = false;
            string strJson = string.Empty;
            while (isSuccess != true)
            {
                EdbLib edb = new EdbLib(StrserviceUrl, Strdbhost, Strappkey, Strappscret, Strtoken);
                Dictionary<String, String> @params = edb.edbGetCommonParams("edbTradeGet");
                if (date_type != "") @params["date_type"] = date_type;
                @params["begin_time"] = begin_time;
                @params["end_time"] = end_time;
                @params["order_status"] = order_status;
                @params["page_no"] = "1";
                @params["page_size"] = page_size;
                LogHelper.WriteFileLog(typeof(Edb_OUTSTOCK_DAL), begin_time + "|" + end_time);
                strJson = edb.edbRequstPost(@params, out isSuccess);
                edb = null;
            }

            if (strJson != string.Empty)
            {
                try
                {
                    JObject jo = (JObject)JsonConvert.DeserializeObject(strJson);
                    JArray arr = (JArray)jo["Success"]["items"]["item"];
                    int total_results = int.Parse(jo["Success"]["total_results"].ToString());
                    if (total_results > 0)
                    {
                        DateTime dt = DateTime.Parse(begin_time);
                        JArray narr = arr;
                        JArray j = null;
                        for (int x = arr.Count - 1; x >= 0; x--)
                        {
                            JObject obj = (JObject)arr[x];
                            if (total_results == int.Parse(page_size))
                            {
                                string delivery_time = obj["delivery_time"].ToString();
                                if (dt < DateTime.Parse(delivery_time))
                                {
                                    dt = DateTime.Parse(delivery_time);
                                }
                            }
                            if (IsExists(obj["tid"].ToString()) == false)
                            {
                                if (j != null)
                                {
                                    //j.Merge((JArray)obj["tid_item"]);
                                    foreach (JObject job in (JArray)obj["tid_item"])
                                    {
                                        j.Add(job);
                                    }
                                }
                                else
                                {
                                    j = (JArray)obj["tid_item"];
                                }
                                obj.Remove("tid_item");
                            }
                            else
                            {
                                arr.Remove(obj);
                            }
                        }
                        if (arr != null && j != null)
                        {
                            DataTable d1 =GetDataTable( JsonToDataTable(arr.ToString()),dtTrade);
                            SqlBulkCopyInsert("tb_Trade", d1);
                            DataTable d2  =GetDataTable( JsonToDataTable(j.ToString()), dtTradeEntry);
                            SqlBulkCopyInsert("tb_TradeEntry", d2);
                        }
                        StringToDB(begin_time, end_time, true);
                        if (total_results == int.Parse(page_size) && dt < DateTime.Parse(end_time) && dt > DateTime.Parse(begin_time))
                        {
                            LoadInfo(date_type, dt.ToString("yyyy-MM-dd HH:mm:ss"), end_time, order_status, StrserviceUrl, Strdbhost, Strappkey, Strappscret, Strtoken);
                        }
                    }
                    else
                    {
                        StringToDB(begin_time, end_time, true);
                    }

                }
                catch(Exception ex)
                {
                    LogHelper.WriteFileLog(typeof(Edb_OUTSTOCK_DAL), ex);
                    LogHelper.WriteFileLog(typeof(Edb_OUTSTOCK_DAL), strJson);
                }
            }
            else
            {
                StringToDB(begin_time, end_time, false);
            }
        }
        private DataTable GetDataTable(DataTable dt,List<string> sList)
        {
            List<string> ColumnNames = new List<string>();
            foreach (DataColumn dc in dt.Columns)
            {
                ColumnNames.Add(dc.ColumnName);
            }
            foreach (string ColumnName in ColumnNames)
            {
                if (sList.Contains(ColumnName) == false)
                {
                    dt.Columns.Remove(ColumnName);
                }
            }
            return dt;
        }
        private bool IsExists(string strBillNO)
        {
            bool isEx = false;
            List<SqlParameter> pars = new List<SqlParameter>();
            pars.Add(new SqlParameter("@tid", strBillNO));
            object o = SqlHelper.ExecuteScalar(strcon, CommandType.Text, "select tid from tb_Trade where tid=@tid union all select tid from tb_Trade_old where tid=@tid", pars.ToArray());
            if (o != null)
            {
                isEx = true;
            }
            return isEx;
        }
    }
}
