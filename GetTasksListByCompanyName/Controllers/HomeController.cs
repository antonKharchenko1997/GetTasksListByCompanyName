﻿using System.Drawing;
using AngleSharp;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc;
using GetTasksListByCompanyName.Models;
using HtmlAgilityPack;
using Newtonsoft.Json;
using OfficeOpenXml;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using PuppeteerSharp;

namespace GetTasksListByCompanyName.Controllers;

public class HomeController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;

    public HomeController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<IActionResult> Index()
    {
        // Fetch the JSON data from the provided URL
        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(
            "https://raw.githubusercontent.com/ankur-lakhmara/Big-Omega-Extension/main/src/resources/leetcode_company_tagged_problems.json");

        if (!response.IsSuccessStatusCode)
            return BadRequest("Failed to retrieve data.");
        
        var content = await response.Content.ReadAsStringAsync();
        // Deserialize the JSON data
        var tasks = JsonConvert.DeserializeObject<Dictionary<string, List<LeetcodeTask>>>(content);

        // Extract the company names
        var companyNames = tasks.Values.SelectMany(c => c.Select(task => task.Company)).Distinct().ToList();
        var orderedCompanyNames = companyNames.OrderBy(i => i);
        // Pass the list of companies to the view
        ViewBag.Companies = orderedCompanyNames;

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> GenerateExcel(string companyName)
    {
        // Fetch the JSON data from the provided URL
        var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(
            "https://raw.githubusercontent.com/codedecks-in/Big-Omega-Extension/main/src/resources/leetcode_company_tagged_problems.json");

        if (!response.IsSuccessStatusCode) return BadRequest("Failed to retrieve data.");
        var content = await response.Content.ReadAsStringAsync();

        // Deserialize the JSON data into a list of objects
        var tasks = JsonConvert.DeserializeObject<Dictionary<string, List<LeetcodeTask>>>(content);

        // Check if the company exists in the tasks dictionary
        var formattedTasks = new List<string>();

        // Filter the tasks based on the company name
        var filteredTasks = FilterTasksByCompany(tasks, companyName, formattedTasks);

        formattedTasks.Sort((t1, t2) =>
        {
            var numOccur1 = GetNumOccurrence(t1);
            var numOccur2 = GetNumOccurrence(t2);
            return numOccur2.CompareTo(numOccur1);
        });


        // Generate the Excel file based on the filtered tasks
        var fileBytes = GenerateExcelFile(filteredTasks);

        // Set the file name for download
        var fileName = $"{companyName}_data.xlsx";

        // Return the Excel file for download
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private List<string> FilterTasksByCompany(Dictionary<string, List<LeetcodeTask>>? tasks, string companyName,
        List<string> formattedTasks)
    {
        if (tasks == null) return formattedTasks;
        foreach (var (nameOfTask, taskItems) in tasks)
        {
            formattedTasks.AddRange(taskItems
                .Where(taskItem => taskItem.Company.Equals(companyName, StringComparison.OrdinalIgnoreCase))
                .Select(taskItem =>
                    $"Link: https://leetcode.com/problems{nameOfTask} , Occur number: {taskItem.NumOccur}, Solved: - , Technique: "));
        }

        return formattedTasks;
    }

    private int GetNumOccurrence(string formattedTask)
    {
        var startIndex = formattedTask.IndexOf("Occur number: ") + 14;
        var endIndex = formattedTask.IndexOf(",", startIndex);
        var numOccurStr = formattedTask.Substring(startIndex, endIndex - startIndex);
        return int.Parse(numOccurStr);
    }

    private byte[] GenerateExcelFile(List<string> tasks)
    {
// Set the LicenseContext to NonCommercial
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        // Create Excel package and worksheet
        using var excelPackage = new ExcelPackage();
        var worksheet = excelPackage.Workbook.Worksheets.Add($"Tasks");

        // Write headers
        worksheet.Cells[1, 1].Value = "Link to task";
        worksheet.Cells[1, 2].Value = "Number of occur";
        worksheet.Cells[1, 3].Value = "Solved";
        worksheet.Cells[1, 4].Value = "Technique";
        worksheet.Cells[1, 5].Value = "Level";
        
        // Format header row in bold
        using (var headerRange = worksheet.Cells[1, 1, 1, 5])
        {
            headerRange.Style.Font.Bold = true;
        }


        // Write data
        var rowIndex = 2;
        foreach (var task in tasks)
        {
            var linkIndex = task.IndexOf("Link: ", StringComparison.Ordinal) + 6;
            var occurIndex = task.IndexOf("Occur number: ", StringComparison.Ordinal) + 14;
            var solvedIndex = task.IndexOf("Solved: ", StringComparison.Ordinal) + 8;
            var techniqueIndex = task.IndexOf("Technique: ", StringComparison.Ordinal) + 11;
            
            var link = task.Substring(linkIndex, occurIndex - linkIndex - 14).Trim().Replace(",", "");
            var occur = task.Substring(occurIndex, solvedIndex - occurIndex - 8).Trim().Replace(",", "");
            var solved = task.Substring(solvedIndex, techniqueIndex - solvedIndex - 11).Trim().Replace(",", "");
            var technique = task.Substring(techniqueIndex).Trim();
            var level = GetTaskLevel(link).Result;
            
            worksheet.Cells[rowIndex, 1].Hyperlink = new Uri(link);
            worksheet.Cells[rowIndex, 1].Style.Font.UnderLine = true;
            worksheet.Cells[rowIndex, 1].Style.Font.Color.SetColor(Color.Blue);

            worksheet.Cells[rowIndex, 2].Value = occur;
            worksheet.Cells[rowIndex, 3].Value = solved;
            worksheet.Cells[rowIndex, 4].Value = technique;
            worksheet.Cells[rowIndex, 5].Value = level;

            rowIndex++;
        }

        // Auto-fit columns
        worksheet.Cells.AutoFitColumns(0);

        return excelPackage.GetAsByteArray();
    }

    private async Task<string> GetTaskLevel(string taskUrl)
    {
        using IWebDriver driver = new ChromeDriver();
        driver.Navigate().GoToUrl(taskUrl);

        // Allow time for JavaScript to load
        Thread.Sleep(3000);

        var levelElement = driver.FindElement(By.ClassName("css-10o4wqw"));

        if (levelElement != null)
        {
            string level = levelElement.Text.Trim();
            Console.WriteLine($"Task Level: {level}");
            return level;
        }

        Console.WriteLine("Level information not found.");
        return "";
    }
}