using QB_Items_Lib;
using QBFC16Lib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using static QB_Items_Test.CommonMethods;

namespace QB_Items_Test
{
    [Collection("Sequential Tests")]
    public class ItemReaderTests
    {
        [Fact]
        public void AddAndReadMultipleItems_FromQuickBooks_And_Verify_Logs()
        {
            const int ITEM_COUNT = 5;
            const int STARTING_COMPANY_ID = 100;
            var itemsToAdd = new List<Item>(ITEM_COUNT); // Pre-allocate capacity

            // 1) Ensure Serilog has released file access before deleting old logs.
            EnsureLogFileClosed();
            DeleteOldLogFiles();
            ResetLogger();

            // 2) Build a list of random Item objects.
            for (int i = 0; i < ITEM_COUNT; i++)
            {
                string randomName = "TestItem_" + Guid.NewGuid().ToString("N")[..8];
                int companyID = STARTING_COMPANY_ID + i;
                decimal salesPrice = 100.00m + i;
                string manufacturerPartNumber = companyID.ToString();
                itemsToAdd.Add(new Item(randomName, salesPrice, manufacturerPartNumber));
            }

            // 3) Add items directly to QuickBooks.
            using (var qbSession = new QuickBooksSession(AppConfig.QB_APP_NAME))
            {
                foreach (var item in itemsToAdd)
                {
                    string qbID = AddItem(qbSession, item.Name, item.SalesPrice, item.ManufacturerPartNumber);
                    item.QB_ID = qbID;
                }
            }

            // 4) Query QuickBooks to retrieve all items.
            var allQBItems = ItemReader.QueryAllItems();

            // 5) Verify all added items are present.
            foreach (var item in itemsToAdd)
            {
                var matchingItem = allQBItems.FirstOrDefault(i => i.QB_ID == item.QB_ID);
                Assert.NotNull(matchingItem);
                Assert.Equal(item.Name, matchingItem.Name);
                Assert.Equal(item.SalesPrice, matchingItem.SalesPrice);
                Assert.Equal(item.ManufacturerPartNumber, matchingItem.ManufacturerPartNumber);
            }

            // 6) Cleanup: Delete the added items.
            using (var qbSession = new QuickBooksSession(AppConfig.QB_APP_NAME))
            {
                foreach (var item in itemsToAdd.Where(i => !string.IsNullOrEmpty(i.QB_ID)))
                {
                    DeleteItem(qbSession, item.QB_ID);
                }
            }

            // 7) Ensure logs are flushed before accessing them.
            EnsureLogFileClosed();

            // 8) Verify log file exists.
            string logFile = GetLatestLogFile();
            EnsureLogFileExists(logFile);

            // 9) Read and check log content.
            string logContents = File.ReadAllText(logFile);
            Assert.Contains("ItemReader Initialized", logContents);
            Assert.Contains("ItemReader Completed", logContents);

            // 10) Check for specific logs per item
            foreach (var item in itemsToAdd)
            {
                string expectedLogMessage = $"Successfully retrieved {item.Name} from QB";
                Assert.Contains(expectedLogMessage, logContents);
            }
        }

        private static string AddItem(QuickBooksSession qbSession, string name, decimal salesPrice, string manufacturerPartNumber)
        {
            IMsgSetRequest requestMsgSet = qbSession.CreateRequestSet();
            IItemInventoryAdd itemAddRq = requestMsgSet.AppendItemInventoryAddRq();
            itemAddRq.Name.SetValue(name);
            itemAddRq.SalesPrice.SetValue((double)salesPrice);
            itemAddRq.ManufacturerPartNumber.SetValue(manufacturerPartNumber);


            // Set the income account reference
 16bfe1f543f7eaf03d2c866dfb4e911722576bb2
            itemAddRq.IncomeAccountRef.FullName.SetValue("Sales");
            itemAddRq.AssetAccountRef.FullName.SetValue("Inventory Asset");


            // Set the COGS account reference
 16bfe1f543f7eaf03d2c866dfb4e911722576bb2
            itemAddRq.COGSAccountRef.FullName.SetValue("Cost of Goods Sold");

            IMsgSetResponse responseMsgSet = qbSession.SendRequest(requestMsgSet);
            return ExtractListIDFromResponse(responseMsgSet);
        }

        private static string ExtractListIDFromResponse(IMsgSetResponse responseMsgSet)
        {
            IResponseList? responseList = responseMsgSet.ResponseList;
            if (responseList == null || responseList.Count == 0)
                throw new Exception("No response from ItemAddRq.");

            IResponse response = responseList.GetAt(0);
            if (response.StatusCode != 0)
                throw new Exception($"ItemAdd failed: {response.StatusMessage}");

            if (response.Detail is IItemInventoryRet inventoryRet)
                return inventoryRet.ListID.GetValue();

            if (response.Detail is IItemNonInventoryRet nonInventoryRet)
                return nonInventoryRet.ListID.GetValue();

            throw new Exception("Unexpected response type after adding Item.");
        }

        private static void DeleteItem(QuickBooksSession qbSession, string listID)
        {
            IMsgSetRequest requestMsgSet = qbSession.CreateRequestSet();
            IListDel listDelRq = requestMsgSet.AppendListDelRq();
            listDelRq.ListDelType.SetValue(ENListDelType.ldtItemInventory);
            listDelRq.ListID.SetValue(listID);

            IMsgSetResponse responseMsgSet = qbSession.SendRequest(requestMsgSet);
            WalkListDelResponse(responseMsgSet, listID);
        }

        private static void WalkListDelResponse(IMsgSetResponse responseMsgSet, string listID)
        {
            IResponseList? responseList = responseMsgSet.ResponseList;
            if (responseList == null || responseList.Count == 0)
                return;

            IResponse response = responseList.GetAt(0);
            if (response.StatusCode == 0 && response.Detail != null)
                Debug.WriteLine($"Successfully deleted Item (ListID: {listID}).");
            else
                throw new Exception($"Error Deleting Item (ListID: {listID}): {response.StatusMessage}. Status code: {response.StatusCode}");
        }
    }
}
