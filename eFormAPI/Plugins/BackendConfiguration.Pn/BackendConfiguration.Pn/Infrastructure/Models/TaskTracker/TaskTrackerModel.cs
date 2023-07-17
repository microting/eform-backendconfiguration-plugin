/*
The MIT License (MIT)

Copyright (c) 2007 - 2023 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

namespace BackendConfiguration.Pn.Infrastructure.Models.TaskTracker;

using System;
using System.Collections.Generic;
using Enums;
using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

/// <summary>
/// Represents a task tracker model.
/// </summary>
public class TaskTrackerModel
{
	/// <summary>
	/// Gets or sets the property of the task tracker model.
	/// </summary>
	public string Property { get; set; }

	/// <summary>
	/// Gets or sets the task name of the task tracker model.
	/// </summary>
	public string TaskName { get; set; }

	/// <summary>
	/// Gets or sets the tags of the task tracker model.
	/// </summary>
	public List<CommonTagModel> Tags { get; set; } 
		= new ();

	/// <summary>
	/// Gets or sets the workers of the task tracker model.
	/// </summary>
	public List<string> Workers { get; set; }
		= new ();

	/// <summary>
	/// Gets or sets the start time of the task tracker model.
	/// </summary>
	public DateTime StartTask { get; set; }

	/// <summary>
	/// Gets or sets the repeat type of the task tracker model.
	/// </summary>
	public RepeatType RepeatType { get; set; }

	/// <summary>
	/// Gets or sets the repeat interval in minutes of the task tracker model.
	/// </summary>
	public int RepeatEvery { get; set; }

	/// <summary>
	/// Gets or sets the deadline of the task tracker model.
	/// </summary>
	public DateTime DeadlineTask { get; set; }

	/// <summary>
	/// Gets or sets the next execution time of the task tracker model.
	/// </summary>
	public DateTime NextExecutionTime { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether the task is expired or not.
	/// </summary>
	public bool TaskIsExpired { get; set; }

	/// <summary>
	/// Gets or sets the SDK case ID.
	/// </summary>
	public int SdkCaseId { get; set; }

	/// <summary>
	/// Gets or sets the template ID.
	/// </summary>
	public int TemplateId { get; set; }

	/// <summary>
	/// Gets or sets the property ID.
	/// </summary>
	public int PropertyId { get; set; }

	/// <summary>
	/// Gets or sets the compliance ID.
	/// </summary>
	public int ComplianceId { get; set; }

	/// <summary>
	/// Gets or sets the area ID.
	/// </summary>
	public int AreaId { get; set; }

	/// <summary>
	/// Gets or sets the area rule ID.
	/// </summary>
	public int AreaRuleId { get; set; }

    /// <summary>
    /// Gets or sets the area rule plan ID.
    /// </summary>
    public int AreaRulePlanId { get; set; }

    public List<TaskTrackerWeeksListModel> Weeks { get; set; }
		= new();
}